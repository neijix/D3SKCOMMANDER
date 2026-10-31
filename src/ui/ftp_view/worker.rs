//! The FTP worker thread: owns the [`FtpSession`], takes [`Cmd`]s, answers with [`Ev`]s.
//! The UI thread never touches the network; this module also holds the pure helpers
//! (remote paths, sorting, store key) that the tests cover.

use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, Sender, channel};
use std::time::{Duration, Instant};

use eframe::egui;

use crate::core::ftp::{FtpSession, FtpSite, RemoteEntry};
use crate::i18n::t;

/// How deep folder transfers and recursive deletes go (guards against loops).
const MAX_DEPTH: usize = 24;
const PROGRESS_EVERY: Duration = Duration::from_millis(100);
const CANCELLED: &str = "cancelled";

/// A remote entry the user picked.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Item {
    pub name: String,
    pub is_dir: bool,
}

pub enum Cmd {
    List,
    Cd(String),
    Up,
    Download {
        items: Vec<Item>,
        local_dir: PathBuf,
    },
    Upload {
        paths: Vec<PathBuf>,
    },
    Mkdir(String),
    Delete {
        name: String,
        is_dir: bool,
    },
    Rename {
        from: String,
        to: String,
    },
    Quit,
}

pub enum Ev {
    Connected,
    Listing {
        pwd: String,
        entries: Vec<RemoteEntry>,
    },
    Progress {
        name: String,
        bytes: u64,
    },
    Message(String),
    Error(String),
    /// A command finished; `reload_local` asks the panels to re-read their folders.
    Done {
        reload_local: bool,
    },
    Disconnected,
}

/// The UI's end of a running worker.
pub struct Handle {
    pub cmd: Sender<Cmd>,
    pub ev: Receiver<Ev>,
    /// Set to abort the running transfer.
    pub cancel: Arc<AtomicBool>,
}

struct Out {
    tx: Sender<Ev>,
    ctx: egui::Context,
}

impl Out {
    fn send(&self, ev: Ev) {
        let _ = self.tx.send(ev);
        self.ctx.request_repaint();
    }
}

/// Starts the worker; it connects at once and reports through the returned handle.
pub fn spawn(site: FtpSite, password: String, ctx: egui::Context) -> Handle {
    let (cmd_tx, cmd_rx) = channel();
    let (ev_tx, ev_rx) = channel();
    let cancel = Arc::new(AtomicBool::new(false));
    let flag = cancel.clone();
    let spawned = std::thread::Builder::new()
        .name("ftp-worker".into())
        .spawn(move || {
            let out = Out { tx: ev_tx, ctx };
            run(&site, &password, &cmd_rx, &out, &flag);
        });
    // On a spawn failure the closure (and `ev_tx`) is dropped: the UI sees a closed channel.
    drop(spawned);
    Handle {
        cmd: cmd_tx,
        ev: ev_rx,
        cancel,
    }
}

fn run(site: &FtpSite, password: &str, rx: &Receiver<Cmd>, out: &Out, cancel: &AtomicBool) {
    let mut session = match FtpSession::connect(site, password) {
        Ok(s) => s,
        Err(e) => {
            out.send(Ev::Error(e));
            out.send(Ev::Disconnected);
            return;
        }
    };
    out.send(Ev::Connected);
    if !site.remote_dir.trim().is_empty()
        && let Err(e) = session.cd(site.remote_dir.trim())
    {
        out.send(Ev::Error(e));
    }
    refresh(&mut session, out);
    out.send(Ev::Done {
        reload_local: false,
    });
    while let Ok(cmd) = rx.recv() {
        if matches!(cmd, Cmd::Quit) {
            break;
        }
        cancel.store(false, Ordering::Relaxed);
        let reload_local = exec(&mut session, cmd, out, cancel);
        out.send(Ev::Done { reload_local });
    }
    session.quit();
    out.send(Ev::Disconnected);
}

fn refresh(session: &mut FtpSession, out: &Out) {
    let listing = session
        .pwd()
        .and_then(|pwd| session.list().map(|entries| (pwd, entries)));
    match listing {
        Ok((pwd, entries)) => out.send(Ev::Listing { pwd, entries }),
        Err(e) => out.send(Ev::Error(e)),
    }
}

/// Runs one command; returns whether the local panels should reload.
fn exec(session: &mut FtpSession, cmd: Cmd, out: &Out, cancel: &AtomicBool) -> bool {
    let mut x = Xfer::new(out, cancel);
    let result = match cmd {
        Cmd::List | Cmd::Quit => Ok(()),
        Cmd::Cd(path) => session.cd(&path),
        Cmd::Up => session.cd(".."),
        Cmd::Download { items, local_dir } => {
            let r = download_all(session, &mut x, &items, &local_dir);
            x.summary(true);
            r
        }
        Cmd::Upload { paths } => {
            let r = upload_all(session, &mut x, &paths);
            x.summary(false);
            r
        }
        Cmd::Mkdir(name) => session.mkdir(&name),
        Cmd::Delete { name, is_dir } => delete_item(session, &name, is_dir, 0),
        Cmd::Rename { from, to } => session.rename(&from, &to),
    };
    if let Err(e) = result {
        out.send(Ev::Error(e));
    }
    refresh(session, out);
    x.downloaded
}

/// Counters and progress throttling of one transfer command.
struct Xfer<'a> {
    out: &'a Out,
    cancel: &'a AtomicBool,
    files: u32,
    skipped: u32,
    bytes: u64,
    downloaded: bool,
}

impl<'a> Xfer<'a> {
    fn new(out: &'a Out, cancel: &'a AtomicBool) -> Self {
        Xfer {
            out,
            cancel,
            files: 0,
            skipped: 0,
            bytes: 0,
            downloaded: false,
        }
    }

    fn check_cancel(&self) -> Result<(), String> {
        if self.cancel.load(Ordering::Relaxed) {
            Err(CANCELLED.to_string())
        } else {
            Ok(())
        }
    }

    /// A progress callback for one file: throttled events, aborts on cancel.
    fn progress<'b>(&'b self, name: &'b str) -> impl FnMut(u64) -> bool + 'b {
        let mut last = Instant::now();
        move |bytes| {
            if last.elapsed() >= PROGRESS_EVERY {
                last = Instant::now();
                self.out.send(Ev::Progress {
                    name: name.to_string(),
                    bytes,
                });
            }
            !self.cancel.load(Ordering::Relaxed)
        }
    }

    fn file_done(&mut self, name: &str, bytes: u64) {
        self.files += 1;
        self.bytes += bytes;
        self.out.send(Ev::Progress {
            name: name.to_string(),
            bytes,
        });
    }

    fn skip(&mut self, what: &str) {
        self.skipped += 1;
        self.out
            .send(Ev::Message(format!("{}: {what}", t("Skipped", "Ignoré"))));
    }

    fn summary(&self, down: bool) {
        if self.files == 0 && self.skipped == 0 {
            return;
        }
        let verb = if down {
            t("Downloaded", "Téléchargé")
        } else {
            t("Uploaded", "Envoyé")
        };
        let mut msg = format!("{verb}: {} / {} B", self.files, self.bytes);
        if self.skipped > 0 {
            msg.push_str(&format!(" ({} {})", self.skipped, t("skipped", "ignorés")));
        }
        self.out.send(Ev::Message(msg));
    }
}

/// Enters `name`, runs `f`, then steps back up whatever `f` returned.
fn in_dir<T>(
    session: &mut FtpSession,
    name: &str,
    f: impl FnOnce(&mut FtpSession) -> Result<T, String>,
) -> Result<T, String> {
    session.cd(name)?;
    let result = f(session);
    let back = session.cd("..");
    let value = result?;
    back?;
    Ok(value)
}

fn download_all(
    session: &mut FtpSession,
    x: &mut Xfer,
    items: &[Item],
    local_dir: &Path,
) -> Result<(), String> {
    x.downloaded = true;
    for item in items {
        download_item(session, x, item, local_dir, 0)?;
    }
    Ok(())
}

fn download_item(
    session: &mut FtpSession,
    x: &mut Xfer,
    item: &Item,
    local_dir: &Path,
    depth: usize,
) -> Result<(), String> {
    x.check_cancel()?;
    if !is_safe_name(&item.name) || depth > MAX_DEPTH {
        x.skip(&item.name);
        return Ok(());
    }
    let target = local_dir.join(&item.name);
    if item.is_dir {
        fs::create_dir_all(&target).map_err(|e| format!("{}: {e}", target.display()))?;
        in_dir(session, &item.name, |s| {
            for e in s.list()? {
                let child = Item {
                    name: e.name,
                    is_dir: e.is_dir,
                };
                download_item(s, x, &child, &target, depth + 1)?;
            }
            Ok(())
        })
    } else {
        let bytes = {
            let mut progress = x.progress(&item.name);
            session.download(&item.name, &target, &mut progress)?
        };
        x.file_done(&item.name, bytes);
        Ok(())
    }
}

fn upload_all(session: &mut FtpSession, x: &mut Xfer, paths: &[PathBuf]) -> Result<(), String> {
    for path in paths {
        upload_item(session, x, path, 0)?;
    }
    Ok(())
}

fn upload_item(
    session: &mut FtpSession,
    x: &mut Xfer,
    path: &Path,
    depth: usize,
) -> Result<(), String> {
    x.check_cancel()?;
    let Some(name) = path
        .file_name()
        .and_then(|n| n.to_str())
        .map(str::to_string)
    else {
        x.skip(&path.display().to_string());
        return Ok(());
    };
    let meta = fs::symlink_metadata(path).map_err(|e| format!("{}: {e}", path.display()))?;
    if meta.file_type().is_symlink() || depth > MAX_DEPTH {
        x.skip(&name);
        return Ok(());
    }
    if meta.is_dir() {
        // The folder may exist already: a failed mkdir surfaces at the `cd` below.
        let _ = session.mkdir(&name);
        let mut children: Vec<PathBuf> = fs::read_dir(path)
            .map_err(|e| format!("{}: {e}", path.display()))?
            .filter_map(|e| e.ok().map(|e| e.path()))
            .collect();
        children.sort();
        in_dir(session, &name, |s| {
            for child in &children {
                upload_item(s, x, child, depth + 1)?;
            }
            Ok(())
        })
    } else if meta.is_file() {
        let bytes = {
            let mut progress = x.progress(&name);
            session.upload(path, &name, &mut progress)?
        };
        x.file_done(&name, bytes);
        Ok(())
    } else {
        x.skip(&name);
        Ok(())
    }
}

/// Deletes a file, or a folder with everything in it.
fn delete_item(
    session: &mut FtpSession,
    name: &str,
    is_dir: bool,
    depth: usize,
) -> Result<(), String> {
    if !is_dir {
        return session.delete(name, false);
    }
    if depth > MAX_DEPTH {
        return Err(format!("{name}: {}", t("too deep", "trop profond")));
    }
    in_dir(session, name, |s| {
        for e in s.list()? {
            delete_item(s, &e.name, e.is_dir, depth + 1)?;
        }
        Ok(())
    })?;
    session.delete(name, true)
}

// ---- pure helpers -------------------------------------------------------------------

/// Where the password of a site lives in the password store. The default port is
/// spelled out so `host` and `host:21` share one entry.
pub fn store_key(user: &str, host: &str, port: u16) -> String {
    let port = if port == 0 { 21 } else { port };
    format!("ftp:{user}@{host}:{port}")
}

/// `name` below the remote folder `base` (`/` and relative bases both work).
pub fn join_remote(base: &str, name: &str) -> String {
    if base.is_empty() {
        name.to_string()
    } else if base.ends_with('/') {
        format!("{base}{name}")
    } else {
        format!("{base}/{name}")
    }
}

/// Folders first, then names case-insensitively.
pub fn sort_entries(entries: &mut [RemoteEntry]) {
    entries.sort_by(|a, b| {
        b.is_dir
            .cmp(&a.is_dir)
            .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
            .then_with(|| a.name.cmp(&b.name))
    });
}

/// A server-supplied name is only used locally when it cannot leave the target folder.
pub fn is_safe_name(name: &str) -> bool {
    !name.is_empty()
        && name != "."
        && name != ".."
        && !name.contains(['/', '\\', '\0'])
        && !(cfg!(windows) && name.contains(':'))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn entry(name: &str, is_dir: bool) -> RemoteEntry {
        RemoteEntry {
            name: name.into(),
            is_dir,
            size: 0,
            modified: None,
            permissions: String::new(),
        }
    }

    #[test]
    fn store_key_format() {
        assert_eq!(
            store_key("bob", "example.org", 2121),
            "ftp:bob@example.org:2121"
        );
        assert_eq!(store_key("bob", "example.org", 0), "ftp:bob@example.org:21");
        assert_eq!(store_key("", "h", 21), "ftp:@h:21");
    }

    #[test]
    fn join_remote_paths() {
        assert_eq!(join_remote("/", "a"), "/a");
        assert_eq!(join_remote("/pub", "a"), "/pub/a");
        assert_eq!(join_remote("/pub/", "a"), "/pub/a");
        assert_eq!(join_remote("", "a"), "a");
    }

    #[test]
    fn folders_sort_first_then_by_name() {
        let mut v = vec![
            entry("b.txt", false),
            entry("Zeta", true),
            entry("a.txt", false),
            entry("alpha", true),
        ];
        sort_entries(&mut v);
        let names: Vec<&str> = v.iter().map(|e| e.name.as_str()).collect();
        assert_eq!(names, ["alpha", "Zeta", "a.txt", "b.txt"]);
    }

    #[test]
    fn unsafe_remote_names_are_refused() {
        assert!(is_safe_name("file.txt"));
        assert!(!is_safe_name(".."));
        assert!(!is_safe_name("../x"));
        assert!(!is_safe_name("a\\b"));
        assert!(!is_safe_name(""));
    }
}
