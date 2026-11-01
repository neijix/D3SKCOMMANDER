//! Blocking FTP / explicit-FTPS client on top of `suppaftp`.
//!
//! One [`FtpSession`] type covers plain FTP and FTPS (`AUTH TLS`, certificates checked
//! against the bundled Mozilla roots). Every operation returns a readable `String` error.
//! Directory listings use MLSD when the server has it and fall back to `LIST` (unix and
//! DOS/IIS formats, see [`parse_list_line`]).

mod listing;

use std::fs::{self, File};
use std::io::{Read, Write};
use std::net::{TcpStream, ToSocketAddrs};
use std::path::Path;
use std::sync::Arc;
use std::time::{Duration, SystemTime};

use serde::{Deserialize, Serialize};
use suppaftp::types::FileType;
use suppaftp::{FtpError, FtpStream, ImplFtpStream, RustlsConnector, RustlsFtpStream, TlsStream};

pub use listing::parse_list_line;
use listing::parse_mlsd_line;

const DEFAULT_PORT: u16 = 21;
const CONNECT_TIMEOUT: Duration = Duration::from_secs(15);
const IO_TIMEOUT: Duration = Duration::from_secs(60);
const ACTIVE_ACCEPT_TIMEOUT: Duration = Duration::from_secs(30);
const CHUNK: usize = 64 * 1024;
const CANCELLED: &str = "cancelled";

/// A saved FTP site. The password is never part of it: it lives in the
/// [`PasswordStore`](crate::core::crypt::PasswordStore) when `save_password` is set.
#[derive(Clone, Debug, Serialize, Deserialize, Default)]
pub struct FtpSite {
    pub name: String,
    pub host: String,
    /// 0 means the default port (21).
    pub port: u16,
    /// Empty = anonymous login.
    pub user: String,
    /// Explicit FTPS (`AUTH TLS`).
    pub secure: bool,
    /// Passive data connections (otherwise active).
    pub passive: bool,
    /// Directory entered after login; empty = stay in the login directory.
    pub remote_dir: String,
    pub save_password: bool,
}

/// One entry of a remote directory listing.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RemoteEntry {
    pub name: String,
    /// Symbolic links are reported as files (a listing cannot tell what they point to).
    pub is_dir: bool,
    pub size: u64,
    pub modified: Option<SystemTime>,
    /// As the server gave it: `drwxr-xr-x` from `LIST`, the octal `UNIX.mode` from MLSD,
    /// empty when unknown.
    pub permissions: String,
}

enum Conn {
    Plain(FtpStream),
    Secure(RustlsFtpStream),
}

/// Run `$body` with `$ftp` bound to the underlying stream, whichever kind it is.
macro_rules! with_conn {
    ($session:expr, $ftp:ident => $body:expr) => {
        match &mut $session.conn {
            Conn::Plain($ftp) => $body,
            Conn::Secure($ftp) => $body,
        }
    };
}

/// A logged-in connection to one FTP(S) server.
pub struct FtpSession {
    conn: Conn,
}

fn err(e: FtpError) -> String {
    e.to_string()
}

fn tcp_connect(host: &str, port: u16) -> Result<TcpStream, String> {
    let addrs = (host, port)
        .to_socket_addrs()
        .map_err(|e| format!("cannot resolve {host}: {e}"))?;
    let mut last = format!("no address found for {host}");
    for addr in addrs {
        match TcpStream::connect_timeout(&addr, CONNECT_TIMEOUT) {
            Ok(stream) => {
                stream
                    .set_read_timeout(Some(IO_TIMEOUT))
                    .and_then(|()| stream.set_write_timeout(Some(IO_TIMEOUT)))
                    .map_err(|e| e.to_string())?;
                return Ok(stream);
            }
            Err(e) => last = format!("cannot connect to {addr}: {e}"),
        }
    }
    Err(last)
}

fn tls_connector() -> Result<RustlsConnector, String> {
    let roots = rustls::RootCertStore {
        roots: webpki_roots::TLS_SERVER_ROOTS.to_vec(),
    };
    let provider = Arc::new(rustls::crypto::ring::default_provider());
    let config = rustls::ClientConfig::builder_with_provider(provider)
        .with_safe_default_protocol_versions()
        .map_err(|e| format!("TLS setup failed: {e}"))?
        .with_root_certificates(roots)
        .with_no_client_auth();
    Ok(RustlsConnector::from(Arc::new(config)))
}

/// Login, binary mode, active/passive choice and the initial directory.
fn prepare<T: TlsStream>(
    mut ftp: ImplFtpStream<T>,
    site: &FtpSite,
    password: &str,
) -> Result<ImplFtpStream<T>, String> {
    if !site.passive {
        ftp = ftp.active_mode(ACTIVE_ACCEPT_TIMEOUT);
    }
    let (user, pass) = if site.user.is_empty() {
        (
            "anonymous",
            if password.is_empty() {
                "anonymous@"
            } else {
                password
            },
        )
    } else {
        (site.user.as_str(), password)
    };
    ftp.login(user, pass).map_err(err)?;
    ftp.transfer_type(FileType::Binary).map_err(err)?;
    if !site.remote_dir.is_empty() {
        ftp.cwd(&site.remote_dir).map_err(err)?;
    }
    Ok(ftp)
}

fn quit_conn<T: TlsStream>(ftp: &mut ImplFtpStream<T>) {
    let _ = ftp.quit();
}

fn list_entries<T: TlsStream>(ftp: &mut ImplFtpStream<T>) -> Result<Vec<RemoteEntry>, String> {
    let mlsd = ftp.mlsd(None);
    entries_from(mlsd, || ftp.list(None).map_err(err))
}

/// MLSD lines when the server answered MLSD, else the lines of a plain LIST.
fn entries_from<E>(
    mlsd: Result<Vec<String>, E>,
    list: impl FnOnce() -> Result<Vec<String>, String>,
) -> Result<Vec<RemoteEntry>, String> {
    if let Ok(lines) = mlsd {
        return Ok(lines.iter().filter_map(|l| parse_mlsd_line(l)).collect());
    }
    Ok(list()?.iter().filter_map(|l| parse_list_line(l)).collect())
}

fn download_to<T: TlsStream>(
    ftp: &mut ImplFtpStream<T>,
    remote: &str,
    local: &Path,
    progress: &mut dyn FnMut(u64) -> bool,
) -> Result<u64, String> {
    let mut file = File::create(local).map_err(|e| format!("{}: {e}", local.display()))?;
    let mut stream = ftp.retr_as_stream(remote).map_err(|e| {
        let _ = fs::remove_file(local);
        err(e)
    })?;
    let mut buf = vec![0u8; CHUNK];
    let mut total = 0u64;
    let mut outcome = Ok(());
    loop {
        let n = match stream.read(&mut buf) {
            Ok(0) => break,
            Ok(n) => n,
            Err(e) => {
                outcome = Err(format!("download failed: {e}"));
                break;
            }
        };
        if let Err(e) = file.write_all(&buf[..n]) {
            outcome = Err(format!("{}: {e}", local.display()));
            break;
        }
        total += n as u64;
        if !progress(total) {
            outcome = Err(CANCELLED.to_string());
            break;
        }
    }
    finish_transfer(ftp, stream, &mut outcome);
    drop(file);
    match outcome {
        Ok(()) => Ok(total),
        Err(e) => {
            let _ = fs::remove_file(local);
            Err(e)
        }
    }
}

/// Close a data transfer: abort after a cancel or an error, finish normally otherwise.
fn finish_transfer<T: TlsStream>(
    ftp: &mut ImplFtpStream<T>,
    stream: suppaftp::TransferStream<T>,
    outcome: &mut Result<(), String>,
) {
    if outcome.is_err() {
        let _ = ftp.abort(stream);
    } else if let Err(e) = stream.finish() {
        *outcome = Err(err(e));
    }
}

fn upload_from<T: TlsStream>(
    ftp: &mut ImplFtpStream<T>,
    local: &Path,
    remote: &str,
    progress: &mut dyn FnMut(u64) -> bool,
) -> Result<u64, String> {
    let mut file = File::open(local).map_err(|e| format!("{}: {e}", local.display()))?;
    let mut stream = ftp.put_with_stream(remote).map_err(err)?;
    let mut buf = vec![0u8; CHUNK];
    let mut total = 0u64;
    let mut outcome = Ok(());
    loop {
        let n = match file.read(&mut buf) {
            Ok(0) => break,
            Ok(n) => n,
            Err(e) => {
                outcome = Err(format!("{}: {e}", local.display()));
                break;
            }
        };
        if let Err(e) = stream.write_all(&buf[..n]) {
            outcome = Err(format!("upload failed: {e}"));
            break;
        }
        total += n as u64;
        if !progress(total) {
            outcome = Err(CANCELLED.to_string());
            break;
        }
    }
    finish_transfer(ftp, stream, &mut outcome);
    match outcome {
        Ok(()) => Ok(total),
        Err(e) => {
            // Do not leave a truncated file on the server.
            let _ = ftp.rm(remote);
            Err(e)
        }
    }
}

impl FtpSession {
    /// Connect and log in. An empty `site.user` logs in anonymously (`password`, when not
    /// empty, is then sent as the e-mail address). `site.secure` upgrades the control and
    /// data connections with `AUTH TLS`; the server certificate must be valid.
    pub fn connect(site: &FtpSite, password: &str) -> Result<Self, String> {
        if site.host.trim().is_empty() {
            return Err("no host given".to_string());
        }
        let host = site.host.trim();
        let port = if site.port == 0 {
            DEFAULT_PORT
        } else {
            site.port
        };
        let tcp = tcp_connect(host, port)?;
        let conn = if site.secure {
            let ftp = RustlsFtpStream::connect_with_stream(tcp).map_err(err)?;
            let ftp = ftp.into_secure(tls_connector()?, host).map_err(err)?;
            Conn::Secure(prepare(ftp, site, password)?)
        } else {
            let ftp = FtpStream::connect_with_stream(tcp).map_err(err)?;
            Conn::Plain(prepare(ftp, site, password)?)
        };
        Ok(FtpSession { conn })
    }

    /// The current remote directory.
    pub fn pwd(&mut self) -> Result<String, String> {
        with_conn!(self, ftp => ftp.pwd().map_err(err))
    }

    pub fn cd(&mut self, path: &str) -> Result<(), String> {
        with_conn!(self, ftp => ftp.cwd(path).map_err(err))
    }

    /// Entries of the current directory (`.` and `..` excluded), in server order.
    pub fn list(&mut self) -> Result<Vec<RemoteEntry>, String> {
        with_conn!(self, ftp => list_entries(ftp))
    }

    /// Download `remote` into `local` (overwritten). `progress` receives the bytes received
    /// so far; returning `false` aborts with the error `"cancelled"`. A failed or cancelled
    /// download removes the partial local file.
    pub fn download(
        &mut self,
        remote: &str,
        local: &Path,
        progress: &mut dyn FnMut(u64) -> bool,
    ) -> Result<u64, String> {
        with_conn!(self, ftp => download_to(ftp, remote, local, progress))
    }

    /// Upload `local` as `remote`. `progress` as for [`FtpSession::download`]; a failed or
    /// cancelled upload removes the partial remote file (best effort).
    pub fn upload(
        &mut self,
        local: &Path,
        remote: &str,
        progress: &mut dyn FnMut(u64) -> bool,
    ) -> Result<u64, String> {
        with_conn!(self, ftp => upload_from(ftp, local, remote, progress))
    }

    pub fn mkdir(&mut self, name: &str) -> Result<(), String> {
        with_conn!(self, ftp => ftp.mkdir(name).map_err(err))
    }

    /// Delete a file, or an empty directory when `is_dir`.
    pub fn delete(&mut self, name: &str, is_dir: bool) -> Result<(), String> {
        with_conn!(self, ftp => if is_dir { ftp.rmdir(name).map_err(err) } else { ftp.rm(name).map_err(err) })
    }

    pub fn rename(&mut self, from: &str, to: &str) -> Result<(), String> {
        with_conn!(self, ftp => ftp.rename(from, to).map_err(err))
    }

    /// Say goodbye and close; errors are ignored (the connection is going away anyway).
    pub fn quit(mut self) {
        with_conn!(self, ftp => quit_conn(ftp));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn lines(v: &[&str]) -> Vec<String> {
        v.iter().map(|l| (*l).to_string()).collect()
    }

    #[test]
    fn listing_prefers_mlsd_and_falls_back_to_list() {
        let mlsd = Ok::<_, ()>(lines(&["type=file;size=3;modify=20240101120000; m.txt"]));
        let got = entries_from(mlsd, || panic!("LIST must not run")).unwrap();
        assert_eq!(got.len(), 1);
        assert_eq!((got[0].name.as_str(), got[0].size), ("m.txt", 3));

        let unix = "-rw-r--r--   1 user group      42 Jan  2  2024 u.txt";
        let dos = "01-02-24  03:04PM       <DIR>          sub";
        let got = entries_from(Err(()), || Ok(lines(&[unix, dos]))).unwrap();
        let names: Vec<_> = got.iter().map(|e| (e.name.as_str(), e.is_dir)).collect();
        assert_eq!(names, [("u.txt", false), ("sub", true)]);

        let failed = entries_from(Err(()), || Err("550 no".to_string()));
        assert_eq!(failed.unwrap_err(), "550 no");
    }

    #[test]
    fn site_serde_round_trip_and_default() {
        let site = FtpSite {
            name: "n".into(),
            host: "h".into(),
            port: 2121,
            user: "u".into(),
            secure: true,
            passive: true,
            remote_dir: "/x".into(),
            save_password: true,
        };
        let json = serde_json::to_string(&site).unwrap();
        assert!(!json.to_lowercase().contains("password\":\""));
        let back: FtpSite = serde_json::from_str(&json).unwrap();
        assert_eq!(back.port, 2121);
        assert!(back.secure && back.passive && back.save_password);
        assert_eq!(FtpSite::default().port, 0);
    }

    #[test]
    fn empty_host_is_refused_without_network() {
        let err = FtpSession::connect(&FtpSite::default(), "").err().unwrap();
        assert!(err.contains("host"));
    }

    #[test]
    fn tls_connector_builds() {
        assert!(tls_connector().is_ok());
    }
}
