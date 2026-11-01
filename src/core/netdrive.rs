//! Network drives: connect a shared folder (SMB / Windows share, and on Linux and macOS any
//! remote file system the OS can mount) and find the local folder to browse it.
//!
//! The mount itself is the operating system's job, done with its own mechanism, so the
//! share then works in every other program too:
//! - Windows: the `WNetAddConnection2` API (a mapped drive letter or a plain UNC path);
//! - macOS: AppleScript `mount volume`, the same as Finder's "Connect to Server";
//! - Linux: `gio mount` (GVFS, the same as the desktop file manager).
//!
//! A password never goes on a command line (other users could read it in the process
//! list): it is passed to the API in memory, or written to the tool's standard input.
//! It is never stored.

use std::path::{Path, PathBuf};

mod os;

pub use os::{connect, disconnect};

/// A remote folder the user typed: `\\server\share\sub`, `//server/share`,
/// `smb://user@server/share`, `sftp://server/path`, …
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Share {
    /// `smb`, `sftp`, `ftp`, `dav`, `davs`, `afp`, `nfs`.
    pub scheme: String,
    /// The user named in the address (`smb://user@server/…`), if any.
    pub user: Option<String>,
    pub server: String,
    /// The share name (first path component), empty for schemes without one (sftp).
    pub share: String,
    /// The rest of the path inside the share, `/`-separated, without leading `/`.
    pub sub: String,
}

/// What the user typed to log in. All may be empty (guest, or the current login).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct Credentials {
    pub user: String,
    pub domain: String,
    pub password: String,
}

const SCHEMES: &[&str] = &["smb", "sftp", "ftp", "dav", "davs", "afp", "nfs"];

/// Reads a share address. Without a scheme it is an SMB share (`\\server\share`,
/// `//server/share` or `server/share`).
pub fn parse(input: &str) -> Result<Share, String> {
    let text = input.trim();
    let (scheme, rest) = match text.split_once("://") {
        Some((s, rest)) => {
            let s = s.to_ascii_lowercase();
            let s = if s == "cifs" { "smb".to_string() } else { s };
            if !SCHEMES.contains(&s.as_str()) {
                return Err(format!("unsupported protocol: {s}://"));
            }
            (s, rest.to_string())
        }
        None => (
            "smb".to_string(),
            text.trim_start_matches(['\\', '/']).replace('\\', "/"),
        ),
    };
    let mut parts = rest.split('/').filter(|p| !p.is_empty());
    let host = parts.next().ok_or("missing server name")?;
    let (user, server) = match host.rsplit_once('@') {
        Some((u, s)) => (Some(u.to_string()), s.to_string()),
        None => (None, host.to_string()),
    };
    if server.is_empty() {
        return Err("missing server name".into());
    }
    let share = parts.next().unwrap_or("").to_string();
    if share.is_empty() && matches!(scheme.as_str(), "smb" | "afp") {
        return Err("missing share name: \\\\server\\share".into());
    }
    let sub = parts.collect::<Vec<_>>().join("/");
    Ok(Share {
        scheme,
        user,
        server,
        share,
        sub,
    })
}

impl Share {
    /// The URI of the share itself (no sub-folder), e.g. `smb://server/share/`.
    pub fn uri(&self) -> String {
        let user = self
            .user
            .as_deref()
            .map(|u| format!("{u}@"))
            .unwrap_or_default();
        if self.share.is_empty() {
            format!("{}://{user}{}/", self.scheme, self.server)
        } else {
            format!("{}://{user}{}/{}/", self.scheme, self.server, self.share)
        }
    }

    /// The Windows UNC path of the share: `\\server\share`.
    pub fn unc(&self) -> String {
        format!(r"\\{}\{}", self.server, self.share)
    }

    /// `base` (where the share is mounted) joined with the sub-folder that was typed.
    pub fn inside(&self, base: &Path) -> PathBuf {
        self.sub
            .split('/')
            .filter(|p| !p.is_empty())
            .fold(base.to_path_buf(), |p, part| p.join(part))
    }
}

/// Quotes `text` as an AppleScript string literal.
pub fn applescript_string(text: &str) -> String {
    let escaped = text.replace('\\', "\\\\").replace('"', "\\\"");
    format!("\"{escaped}\"")
}

/// The AppleScript that mounts `share` on macOS. Without a user, macOS asks for the login
/// itself (or uses the Keychain).
pub fn mac_mount_script(share: &Share, creds: &Credentials) -> String {
    let mut uri = share.uri();
    if share.user.is_none() && !creds.user.is_empty() {
        let mut with_user = share.clone();
        let user = if creds.domain.is_empty() {
            creds.user.clone()
        } else {
            format!("{};{}", creds.domain, creds.user)
        };
        with_user.user = Some(user);
        uri = with_user.uri();
    }
    // macOS mounts WebDAV through http(s) URLs.
    let uri = match uri.split_once("://") {
        Some(("dav", rest)) => format!("http://{rest}"),
        Some(("davs", rest)) => format!("https://{rest}"),
        _ => uri,
    };
    let mut script = format!("mount volume {}", applescript_string(&uri));
    if !creds.password.is_empty() {
        script.push_str(" with password ");
        script.push_str(&applescript_string(&creds.password));
    }
    script
}

/// The answers `gio mount` reads on its standard input: user, domain, password, one per
/// line. An empty line takes gio's default (the current user, the default domain).
pub fn gio_answers(creds: &Credentials) -> String {
    format!("{}\n{}\n{}\n", creds.user, creds.domain, creds.password)
}

/// The `local path:` line of `gio info <uri>`: where GVFS shows the mounted share.
pub fn gio_local_path(info_output: &str) -> Option<PathBuf> {
    info_output
        .lines()
        .find_map(|l| l.strip_prefix("local path: "))
        .map(|p| PathBuf::from(p.trim()))
}

/// The URI of the GVFS mount a local path belongs to: `path` under
/// `…/gvfs/smb-share:server=nas,share=media/…` gives `smb://nas/media/`.
pub fn gvfs_uri(path: &Path) -> Option<String> {
    let mut comps = path.components().map(|c| c.as_os_str().to_string_lossy());
    comps.find(|c| c == "gvfs")?;
    let mount = comps.next()?;
    let (kind, params) = mount.split_once(':')?;
    let get = |key: &str| {
        params
            .split(',')
            .find_map(|kv| kv.strip_prefix(key)?.strip_prefix('='))
            .map(unescape_gvfs)
    };
    if kind == "smb-share" {
        return Some(format!("smb://{}/{}/", get("server")?, get("share")?));
    }
    let host = get("host")?;
    let scheme = if kind == "dav" && get("ssl").as_deref() == Some("true") {
        "davs"
    } else {
        kind
    };
    let user = get("user").map(|u| format!("{u}@")).unwrap_or_default();
    let port = get("port").map(|p| format!(":{p}")).unwrap_or_default();
    Some(format!("{scheme}://{user}{host}{port}/"))
}

/// Decodes `%XX` escapes (GVFS mount names, URLs in the macOS `mount` output).
fn unescape_gvfs(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        let hex = text
            .get(i + 1..i + 3)
            .and_then(|h| u8::from_str_radix(h, 16).ok());
        match (bytes[i], hex) {
            (b'%', Some(b)) => {
                out.push(b);
                i += 3;
            }
            (b, _) => {
                out.push(b);
                i += 1;
            }
        }
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// One line of the macOS `mount` command: `//user@server/share on /Volumes/share (smbfs, …)`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MacMount {
    pub source: String,
    pub point: PathBuf,
    pub fs_type: String,
}

/// The network mounts listed by the macOS `mount` command.
pub fn mac_network_mounts(mount_output: &str) -> Vec<MacMount> {
    mount_output
        .lines()
        .filter_map(|l| {
            let (source, rest) = l.split_once(" on ")?;
            let (point, opts) = rest.rsplit_once(" (")?;
            let fs_type = opts.split([',', ')']).next()?.trim().to_string();
            matches!(fs_type.as_str(), "smbfs" | "afpfs" | "nfs" | "webdav").then(|| MacMount {
                source: source.to_string(),
                point: PathBuf::from(point),
                fs_type,
            })
        })
        .collect()
}

/// Where macOS mounted `share`, from the `mount` command's output.
pub fn mac_mount_point(mount_output: &str, share: &Share) -> Option<PathBuf> {
    let server = share.server.to_ascii_lowercase();
    let name = share.share.to_ascii_lowercase();
    mac_network_mounts(mount_output)
        .into_iter()
        .find(|m| {
            let src = m.source.to_ascii_lowercase();
            let host = src
                .trim_start_matches('/')
                .split('/')
                .next()
                .unwrap_or("")
                .rsplit('@')
                .next()
                .unwrap_or("")
                .to_string();
            let tail = src.rsplit('/').next().unwrap_or("");
            (host == server || host.starts_with(&format!("{server}.")))
                && (name.is_empty() || unescape_gvfs(tail).eq_ignore_ascii_case(&name))
        })
        .map(|m| m.point)
}

/// The network mount of macOS that contains `path` (the deepest one).
pub fn mac_mount_containing(mount_output: &str, path: &Path) -> Option<PathBuf> {
    mac_network_mounts(mount_output)
        .into_iter()
        .map(|m| m.point)
        .filter(|p| path.starts_with(p))
        .max_by_key(|p| p.components().count())
}

/// The share part of a Windows path: `\\server\share` for a UNC path, `Z:` for a drive.
pub fn windows_root(path: &str) -> Option<String> {
    let p = path.replace('/', "\\");
    if let Some(rest) = p.strip_prefix(r"\\") {
        let mut parts = rest.split('\\').filter(|s| !s.is_empty());
        let (server, share) = (parts.next()?, parts.next()?);
        if server == "?" || server == "." {
            return None;
        }
        return Some(format!(r"\\{server}\{share}"));
    }
    let mut chars = p.chars();
    match (chars.next(), chars.next()) {
        (Some(l), Some(':')) if l.is_ascii_alphabetic() => {
            Some(format!("{}:", l.to_ascii_uppercase()))
        }
        _ => None,
    }
}

/// A drive letter typed as `z`, `Z:` or `Z:\` → `Z:`; empty → `None`.
pub fn drive_letter(text: &str) -> Result<Option<String>, String> {
    let t = text
        .trim()
        .trim_end_matches(['\\', '/'])
        .trim_end_matches(':');
    match t.len() {
        0 => Ok(None),
        1 if t.chars().all(|c| c.is_ascii_alphabetic()) => {
            Ok(Some(format!("{}:", t.to_ascii_uppercase())))
        }
        _ => Err(format!("not a drive letter: {text}")),
    }
}

#[cfg(test)]
mod tests;
