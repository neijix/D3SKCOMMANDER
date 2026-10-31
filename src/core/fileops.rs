//! Background copy / move / delete with progress, cancel and an overwrite-conflict flow.
//!
//! [`run`] is the synchronous core: it reports through an `on_event` callback and blocks on
//! the reply channel of every [`OpEvent::Conflict`]. [`spawn`] runs it on a thread and hands
//! the events to the UI through a channel. The copy / move engine lives in `transfer`.

use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{self, Receiver, Sender};
use std::sync::{Arc, Mutex, PoisonError};
use std::time::{Duration, Instant, SystemTime};

mod transfer;

#[cfg(test)]
mod tests;

/// Minimum delay between two `Progress` events inside one file.
const PROGRESS_INTERVAL: Duration = Duration::from_millis(50);

/// What to do with the sources.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum OpKind {
    Copy,
    Move,
    Delete { to_trash: bool },
}

/// One operation: the sources and where they go.
#[derive(Clone, Debug)]
pub struct OpRequest {
    pub kind: OpKind,
    pub sources: Vec<PathBuf>,
    /// `None` for `Delete`.
    pub dest_dir: Option<PathBuf>,
    /// A single source copied/moved under a new name.
    pub rename_to: Option<String>,
}

/// The user's answer to an overwrite conflict.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum ConflictChoice {
    Overwrite,
    Skip,
    OverwriteAll,
    SkipAll,
    OverwriteAllOlder,
    RenameNew,
    Cancel,
}

/// One side of a conflict, as shown in the overwrite dialog.
#[derive(Clone, Debug)]
pub struct FileInfo {
    pub path: PathBuf,
    pub size: u64,
    pub modified: Option<SystemTime>,
}

/// Running totals. `done_files` counts every file handled (done, skipped or failed), so the
/// bar reaches `total_files` however the operation ends.
#[derive(Clone, Debug)]
pub struct OpProgress {
    pub total_bytes: u64,
    pub done_bytes: u64,
    pub total_files: u64,
    pub done_files: u64,
    pub current: PathBuf,
}

#[derive(Clone, Debug)]
pub enum OpEvent {
    Progress(OpProgress),
    /// The target exists: answer on `reply` (a dropped reply means `Cancel`).
    Conflict {
        source: FileInfo,
        target: FileInfo,
        reply: Sender<ConflictChoice>,
    },
    /// The operation continues with the next file.
    Error {
        path: PathBuf,
        message: String,
    },
    /// Always the last event. `done_files` counts files actually copied, moved or deleted.
    Finished {
        done_files: u64,
        errors: u64,
        cancelled: bool,
    },
}

/// Policy remembered from an `*All` answer.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Policy {
    Overwrite,
    Skip,
    Older,
}

/// The operation was cancelled (flag, `Cancel` answer or dropped reply).
struct Cancelled;
type Flow = Result<(), Cancelled>;

struct Engine<'a> {
    cancel: &'a AtomicBool,
    on_event: &'a mut dyn FnMut(OpEvent),
    total_bytes: u64,
    total_files: u64,
    done_bytes: u64,
    handled_files: u64,
    done_files: u64,
    errors: u64,
    current: PathBuf,
    last_emit: Option<Instant>,
    policy: Option<Policy>,
    cancelled: bool,
}

/// Runs the request on a background thread; the receiver ends after `Finished`.
pub fn spawn(req: OpRequest, cancel: Arc<AtomicBool>) -> Receiver<OpEvent> {
    let (tx, rx) = mpsc::channel();
    std::thread::spawn(move || {
        let stop = Arc::clone(&cancel);
        run(&req, &cancel, &mut |event| {
            // The receiver is gone: nobody watches this operation any more.
            if tx.send(event).is_err() {
                stop.store(true, Ordering::SeqCst);
            }
        });
    });
    rx
}

/// Synchronous core of [`spawn`]. Blocks on every `Conflict` reply.
pub fn run(req: &OpRequest, cancel: &AtomicBool, on_event: &mut dyn FnMut(OpEvent)) {
    let mut engine = Engine::new(cancel, on_event);
    // A `Cancelled` result only stops the loop; `finish` reports it.
    let _ = match req.kind {
        OpKind::Delete { to_trash } => engine.delete_all(&req.sources, to_trash),
        OpKind::Copy => engine.transfer_all(req, false),
        OpKind::Move => engine.transfer_all(req, true),
    };
    engine.finish();
}

/// `a.txt` -> `a (2).txt`, `a (3).txt`… the first name not taken in `dir` (the name itself
/// when it is free). A leading dot is not an extension: `.rc` -> `.rc (2)`.
pub fn unique_name(dir: &Path, name: &str) -> PathBuf {
    let first = dir.join(name);
    if !exists(&first) {
        return first;
    }
    let (stem, ext) = match name.rfind('.') {
        Some(i) if i > 0 => name.split_at(i),
        _ => (name, ""),
    };
    (2u64..)
        .map(|n| dir.join(format!("{stem} ({n}){ext}")))
        .find(|p| !exists(p))
        .unwrap_or(first)
}

fn exists(path: &Path) -> bool {
    fs::symlink_metadata(path).is_ok()
}

static LOG_FILE: Mutex<Option<PathBuf>> = Mutex::new(None);

/// Sets (or clears) the log file that gets one line per finished file.
pub fn set_log_file(path: Option<PathBuf>) {
    *LOG_FILE.lock().unwrap_or_else(PoisonError::into_inner) = path;
}

/// Appends `date\tOP\tsrc\tdst\tOK|ERROR msg`; logging never fails the operation.
fn log_line(op: &str, src: &Path, dst: Option<&Path>, outcome: Result<(), &str>) {
    let guard = LOG_FILE.lock().unwrap_or_else(PoisonError::into_inner);
    let Some(path) = guard.as_ref() else { return };
    let result = match outcome {
        Ok(()) => "OK".to_string(),
        Err(msg) => format!("ERROR {}", msg.replace(['\t', '\n', '\r'], " ")),
    };
    let line = format!(
        "{}\t{}\t{}\t{}\t{}\n",
        chrono::Local::now().format("%Y-%m-%d %H:%M:%S"),
        op,
        src.to_string_lossy(),
        dst.map(|d| d.to_string_lossy().into_owned())
            .unwrap_or_default(),
        result
    );
    if let Ok(mut file) = fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = file.write_all(line.as_bytes());
    }
}

/// `(files, bytes)` under `path`, without following symlinks. Unreadable entries count for 0.
fn count_tree(path: &Path) -> (u64, u64) {
    walkdir::WalkDir::new(path)
        .follow_links(false)
        .into_iter()
        .filter_map(Result::ok)
        .filter(|entry| !entry.file_type().is_dir())
        .fold((0, 0), |(files, bytes), entry| {
            let len = entry.metadata().map(|m| m.len()).unwrap_or(0);
            (files + 1, bytes + len)
        })
}

impl<'a> Engine<'a> {
    fn new(cancel: &'a AtomicBool, on_event: &'a mut dyn FnMut(OpEvent)) -> Self {
        Engine {
            cancel,
            on_event,
            total_bytes: 0,
            total_files: 0,
            done_bytes: 0,
            handled_files: 0,
            done_files: 0,
            errors: 0,
            current: PathBuf::new(),
            last_emit: None,
            policy: None,
            cancelled: false,
        }
    }

    /// Counts every source up front and sets the totals; returns the per-source counts.
    fn prescan(&mut self, sources: &[PathBuf]) -> Vec<(u64, u64)> {
        let counts: Vec<(u64, u64)> = sources.iter().map(|s| count_tree(s)).collect();
        self.total_files = counts.iter().map(|c| c.0).sum();
        self.total_bytes = counts.iter().map(|c| c.1).sum();
        counts
    }

    fn check_cancel(&mut self) -> Flow {
        if self.cancel.load(Ordering::SeqCst) {
            self.cancelled = true;
            return Err(Cancelled);
        }
        Ok(())
    }

    /// Sends a `Progress` event; unless `force`, at most one per `PROGRESS_INTERVAL`.
    fn emit_progress(&mut self, force: bool) {
        let due = self
            .last_emit
            .is_none_or(|t| t.elapsed() >= PROGRESS_INTERVAL);
        if !(force || due) {
            return;
        }
        self.last_emit = Some(Instant::now());
        let progress = OpProgress {
            total_bytes: self.total_bytes,
            done_bytes: self.done_bytes,
            total_files: self.total_files,
            done_files: self.handled_files,
            current: self.current.clone(),
        };
        (self.on_event)(OpEvent::Progress(progress));
    }

    /// Reports an error on `path` and logs it; the caller moves on to the next file.
    fn fail(&mut self, op: &str, path: &Path, dst: Option<&Path>, message: &str) {
        self.errors += 1;
        log_line(op, path, dst, Err(message));
        (self.on_event)(OpEvent::Error {
            path: path.to_path_buf(),
            message: message.to_string(),
        });
    }

    /// `files`/`bytes` were copied, moved or deleted successfully.
    fn succeed(&mut self, op: &str, src: &Path, dst: Option<&Path>, files: u64, bytes: u64) {
        self.done_files += files;
        self.handled_files += files;
        self.done_bytes += bytes;
        log_line(op, src, dst, Ok(()));
        self.emit_progress(true);
    }

    fn delete_all(&mut self, sources: &[PathBuf], to_trash: bool) -> Flow {
        let counts = self.prescan(sources);
        for (src, (files, bytes)) in sources.iter().zip(counts) {
            self.check_cancel()?;
            self.current = src.clone();
            self.emit_progress(true);
            let result = if to_trash {
                trash::delete_all([src]).map_err(|e| e.to_string())
            } else {
                remove_path(src).map_err(|e| e.to_string())
            };
            match result {
                Ok(()) => self.succeed("DELETE", src, None, files, bytes),
                Err(message) => {
                    self.fail("DELETE", src, None, &message);
                    self.handled_files += files;
                    self.done_bytes += bytes;
                }
            }
        }
        Ok(())
    }

    fn finish(&mut self) {
        self.emit_progress(true);
        (self.on_event)(OpEvent::Finished {
            done_files: self.done_files,
            errors: self.errors,
            cancelled: self.cancelled,
        });
    }
}

/// Permanent delete; a symlink is removed itself, never followed.
fn remove_path(path: &Path) -> std::io::Result<()> {
    let file_type = fs::symlink_metadata(path)?.file_type();
    if file_type.is_symlink() {
        // A directory symlink needs `remove_dir` on Windows.
        fs::remove_file(path).or_else(|_| fs::remove_dir(path))
    } else if file_type.is_dir() {
        fs::remove_dir_all(path)
    } else {
        fs::remove_file(path)
    }
}
