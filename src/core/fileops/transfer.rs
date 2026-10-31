//! The copy / move half of the engine: trees, files, symlinks and the conflict flow.

use super::*;
use std::ffi::OsString;
use std::io::{self, ErrorKind, Read};
use std::path::Component;

use filetime::FileTime;

/// Copy buffer: 1 MiB.
pub(super) const BUFFER_SIZE: usize = 1024 * 1024;

/// How long to block on a conflict reply before looking at the cancel flag again.
const REPLY_POLL: Duration = Duration::from_millis(100);

/// What to do with one conflicting target.
enum Decision {
    Write(PathBuf),
    Skip,
}

enum WriteError {
    Cancelled,
    Io(io::Error),
}

impl From<io::Error> for WriteError {
    fn from(e: io::Error) -> Self {
        WriteError::Io(e)
    }
}

impl Engine<'_> {
    /// Copies or moves every source into `req.dest_dir` (created when missing).
    pub(super) fn transfer_all(&mut self, req: &OpRequest, moving: bool) -> Flow {
        let op = if moving { "MOVE" } else { "COPY" };
        let Some(dest_dir) = req.dest_dir.as_deref() else {
            self.fail(op, Path::new(""), None, "no destination folder");
            return Ok(());
        };
        let dest = match fs::create_dir_all(dest_dir).and_then(|()| dest_dir.canonicalize()) {
            Ok(dest) => dest,
            Err(e) => {
                self.fail(op, dest_dir, None, &e.to_string());
                return Ok(());
            }
        };
        let rename = match single_name(req) {
            Ok(name) => name,
            Err(message) => {
                self.fail(op, Path::new(""), None, &message);
                return Ok(());
            }
        };
        let counts = self.prescan(&req.sources);
        for (src, count) in req.sources.iter().zip(counts) {
            self.check_cancel()?;
            let name = rename
                .clone()
                .or_else(|| src.file_name().map(OsString::from));
            let Some(name) = name else {
                self.fail(op, src, None, "the path has no file name");
                self.skip_counts(count);
                continue;
            };
            self.transfer_one(op, src, &dest.join(name), moving, count)?;
        }
        Ok(())
    }

    fn transfer_one(
        &mut self,
        op: &str,
        src: &Path,
        target: &Path,
        moving: bool,
        count: (u64, u64),
    ) -> Flow {
        self.current = src.to_path_buf();
        let meta = match fs::symlink_metadata(src) {
            Ok(meta) => meta,
            Err(e) => {
                self.fail(op, src, Some(target), &e.to_string());
                self.skip_counts(count);
                return Ok(());
            }
        };
        let resolved = resolve(src);
        let refusal = if resolved == target {
            Some("source and destination are the same")
        } else if meta.is_dir() && target.starts_with(&resolved) {
            Some("cannot copy a folder into itself")
        } else {
            None
        };
        if let Some(message) = refusal {
            self.fail(op, src, Some(target), message);
            self.skip_counts(count);
            return Ok(());
        }
        if moving && !exists(target) && fs::rename(src, target).is_ok() {
            self.succeed(op, src, Some(target), count.0, count.1);
            return Ok(());
        }
        self.copy_tree(op, src, target, moving)
    }

    /// A refused source still counts as handled so the bar can finish.
    fn skip_counts(&mut self, (files, bytes): (u64, u64)) {
        self.handled_files += files;
        self.done_bytes += bytes;
    }

    fn fail_file(&mut self, op: &str, src: &Path, dst: &Path, message: &str, size: u64) {
        self.fail(op, src, Some(dst), message);
        self.skip_counts((1, size));
        self.emit_progress(true);
    }

    fn copy_tree(&mut self, op: &str, src: &Path, dst: &Path, moving: bool) -> Flow {
        self.check_cancel()?;
        let mut meta = match fs::symlink_metadata(src) {
            Ok(meta) => meta,
            Err(e) => {
                self.fail_file(op, src, dst, &e.to_string(), 0);
                return Ok(());
            }
        };
        if meta.file_type().is_symlink() {
            if cfg!(unix) {
                return self.copy_link(op, src, dst, &meta, moving);
            }
            // Elsewhere a link is replaced by what it points to.
            match fs::metadata(src) {
                Ok(target_meta) => meta = target_meta,
                Err(e) => {
                    self.fail_file(op, src, dst, &e.to_string(), 0);
                    return Ok(());
                }
            }
        }
        if meta.is_dir() {
            self.copy_dir(op, src, dst, &meta, moving)
        } else {
            self.copy_file(op, src, dst, &meta, moving)
        }
    }

    fn copy_dir(
        &mut self,
        op: &str,
        src: &Path,
        dst: &Path,
        meta: &fs::Metadata,
        moving: bool,
    ) -> Flow {
        let created = match fs::symlink_metadata(dst) {
            Ok(m) if m.is_dir() => false,
            Ok(_) => {
                self.fail(op, src, Some(dst), "the target exists and is not a folder");
                return Ok(());
            }
            Err(_) => {
                if let Err(e) = fs::create_dir(dst) {
                    self.fail(op, src, Some(dst), &e.to_string());
                    return Ok(());
                }
                true
            }
        };
        // Collected first: a move removes entries while we walk the folder.
        let entries: Vec<_> = match fs::read_dir(src) {
            Ok(entries) => entries.collect(),
            Err(e) => {
                self.fail(op, src, Some(dst), &e.to_string());
                return Ok(());
            }
        };
        for entry in entries {
            match entry {
                Ok(entry) => {
                    self.copy_tree(op, &entry.path(), &dst.join(entry.file_name()), moving)?
                }
                Err(e) => self.fail(op, src, Some(dst), &e.to_string()),
            }
        }
        if created {
            apply_metadata(dst, meta);
        }
        if moving {
            // Only succeeds once everything in it was moved away (skips keep it).
            let _ = fs::remove_dir(src);
        }
        Ok(())
    }

    fn copy_file(
        &mut self,
        op: &str,
        src: &Path,
        dst: &Path,
        meta: &fs::Metadata,
        moving: bool,
    ) -> Flow {
        self.current = src.to_path_buf();
        let size = meta.len();
        let Some(dst) = self.resolve_target(op, src, meta, dst)? else {
            return Ok(());
        };
        self.emit_progress(true);
        if moving && fs::rename(src, &dst).is_ok() {
            self.succeed(op, src, Some(&dst), 1, size);
            return Ok(());
        }
        let start = self.done_bytes;
        match self.write_file(src, &dst, meta) {
            Ok(()) => self.finish_copy(op, src, &dst, moving),
            Err(WriteError::Cancelled) => {
                self.cancelled = true;
                return Err(Cancelled);
            }
            Err(WriteError::Io(e)) => {
                self.done_bytes = start;
                self.fail_file(op, src, &dst, &e.to_string(), size);
            }
        }
        Ok(())
    }

    /// A copy is on disk: for a move, drop the source — only now that the copy succeeded.
    fn finish_copy(&mut self, op: &str, src: &Path, dst: &Path, moving: bool) {
        let removed = if moving { fs::remove_file(src) } else { Ok(()) };
        match removed {
            Ok(()) => self.succeed(op, src, Some(dst), 1, 0),
            Err(e) => {
                let message = format!("copied, but the source could not be removed: {e}");
                self.fail(op, src, Some(dst), &message);
                self.handled_files += 1;
            }
        }
    }

    /// Streams `src` to `dst`; a failed or cancelled copy leaves no partial file behind.
    fn write_file(
        &mut self,
        src: &Path,
        dst: &Path,
        meta: &fs::Metadata,
    ) -> Result<(), WriteError> {
        let mut input = fs::File::open(src)?;
        // Never write through an existing symlink.
        if fs::symlink_metadata(dst).is_ok_and(|m| m.file_type().is_symlink()) {
            fs::remove_file(dst)?;
        }
        let mut output = fs::File::create(dst)?;
        let result = self.pump(&mut input, &mut output, meta.len());
        drop(output);
        if result.is_err() {
            let _ = fs::remove_file(dst);
            return result;
        }
        apply_metadata(dst, meta);
        Ok(())
    }

    fn pump(
        &mut self,
        input: &mut fs::File,
        output: &mut fs::File,
        size: u64,
    ) -> Result<(), WriteError> {
        use std::io::Write;
        let chunk = usize::try_from(size).map_or(BUFFER_SIZE, |s| s.clamp(4096, BUFFER_SIZE));
        let mut buf = vec![0u8; chunk];
        loop {
            if self.cancel.load(Ordering::SeqCst) {
                return Err(WriteError::Cancelled);
            }
            let n = match input.read(&mut buf) {
                Ok(0) => return Ok(()),
                Ok(n) => n,
                Err(e) if e.kind() == ErrorKind::Interrupted => continue,
                Err(e) => return Err(e.into()),
            };
            output.write_all(&buf[..n])?;
            self.done_bytes += n as u64;
            self.emit_progress(false);
        }
    }

    /// Copies the link itself (Unix only; other platforms follow it in `copy_tree`).
    fn copy_link(
        &mut self,
        op: &str,
        src: &Path,
        dst: &Path,
        meta: &fs::Metadata,
        moving: bool,
    ) -> Flow {
        self.current = src.to_path_buf();
        let Some(dst) = self.resolve_target(op, src, meta, dst)? else {
            return Ok(());
        };
        if moving && fs::rename(src, &dst).is_ok() {
            self.succeed(op, src, Some(&dst), 1, 0);
            return Ok(());
        }
        let made = fs::read_link(src).and_then(|link| {
            if exists(&dst) {
                fs::remove_file(&dst)?;
            }
            make_symlink(&link, &dst)
        });
        match made {
            Ok(()) => self.finish_copy(op, src, &dst, moving),
            Err(e) => self.fail_file(op, src, &dst, &e.to_string(), 0),
        }
        Ok(())
    }

    /// Settles what an existing `dst` means: the path to write, or `None` when the file is
    /// skipped or refused (already accounted for).
    fn resolve_target(
        &mut self,
        op: &str,
        src: &Path,
        meta: &fs::Metadata,
        dst: &Path,
    ) -> Result<Option<PathBuf>, Cancelled> {
        let Ok(target_meta) = fs::symlink_metadata(dst) else {
            return Ok(Some(dst.to_path_buf()));
        };
        if target_meta.is_dir() {
            self.fail_file(op, src, dst, "the target is a folder", meta.len());
            return Ok(None);
        }
        match self.decide(src, meta, dst, &target_meta)? {
            Decision::Write(path) => Ok(Some(path)),
            Decision::Skip => {
                self.skip_counts((1, meta.len()));
                self.emit_progress(true);
                Ok(None)
            }
        }
    }

    /// Applies the remembered policy or asks the user and blocks on the answer.
    fn decide(
        &mut self,
        src: &Path,
        meta: &fs::Metadata,
        dst: &Path,
        target_meta: &fs::Metadata,
    ) -> Result<Decision, Cancelled> {
        let older = |dst: &Path| match (meta.modified(), target_meta.modified()) {
            (Ok(source), Ok(target)) if target < source => Decision::Write(dst.to_path_buf()),
            _ => Decision::Skip,
        };
        match self.policy {
            Some(Policy::Overwrite) => return Ok(Decision::Write(dst.to_path_buf())),
            Some(Policy::Skip) => return Ok(Decision::Skip),
            Some(Policy::Older) => return Ok(older(dst)),
            None => {}
        }
        // The sender moves into the event: a closure that drops it unanswered disconnects.
        let (reply, answer) = mpsc::channel();
        (self.on_event)(OpEvent::Conflict {
            source: file_info(src, meta),
            target: file_info(dst, target_meta),
            reply,
        });
        Ok(match self.wait_for(&answer) {
            ConflictChoice::Overwrite => Decision::Write(dst.to_path_buf()),
            ConflictChoice::Skip => Decision::Skip,
            ConflictChoice::OverwriteAll => {
                self.policy = Some(Policy::Overwrite);
                Decision::Write(dst.to_path_buf())
            }
            ConflictChoice::SkipAll => {
                self.policy = Some(Policy::Skip);
                Decision::Skip
            }
            ConflictChoice::OverwriteAllOlder => {
                self.policy = Some(Policy::Older);
                older(dst)
            }
            ConflictChoice::RenameNew => {
                let dir = dst.parent().unwrap_or(Path::new(""));
                // Lossy only for a name that is not UTF-8: the new name stays unique.
                let name = dst.file_name().unwrap_or_default().to_string_lossy();
                Decision::Write(unique_name(dir, &name))
            }
            ConflictChoice::Cancel => {
                self.cancelled = true;
                return Err(Cancelled);
            }
        })
    }

    /// Blocks on the reply; a dropped sender or a raised cancel flag means `Cancel`.
    fn wait_for(&self, answer: &Receiver<ConflictChoice>) -> ConflictChoice {
        loop {
            match answer.recv_timeout(REPLY_POLL) {
                Ok(choice) => return choice,
                Err(mpsc::RecvTimeoutError::Disconnected) => return ConflictChoice::Cancel,
                Err(mpsc::RecvTimeoutError::Timeout) => {
                    if self.cancel.load(Ordering::SeqCst) {
                        return ConflictChoice::Cancel;
                    }
                }
            }
        }
    }
}

/// The new name of a single source, validated: one plain path component.
fn single_name(req: &OpRequest) -> Result<Option<OsString>, String> {
    match req.rename_to.as_deref() {
        Some(name) if req.sources.len() == 1 && !name.is_empty() => {
            let mut parts = Path::new(name).components();
            match (parts.next(), parts.next()) {
                (Some(Component::Normal(part)), None) => Ok(Some(part.to_os_string())),
                _ => Err(format!("invalid file name: {name}")),
            }
        }
        _ => Ok(None),
    }
}

/// The source with its parent canonicalized (the entry itself is never followed).
fn resolve(path: &Path) -> PathBuf {
    let parent = match path.parent() {
        Some(p) if p.as_os_str().is_empty() => Path::new("."),
        Some(p) => p,
        None => return path.to_path_buf(),
    };
    match (parent.canonicalize(), path.file_name()) {
        (Ok(parent), Some(name)) => parent.join(name),
        _ => path.to_path_buf(),
    }
}

fn file_info(path: &Path, meta: &fs::Metadata) -> FileInfo {
    FileInfo {
        path: path.to_path_buf(),
        size: meta.len(),
        modified: meta.modified().ok(),
    }
}

/// Copies times, then permissions (a read-only file could not be re-dated). Best effort:
/// some file systems keep neither.
fn apply_metadata(dst: &Path, meta: &fs::Metadata) {
    let _ = filetime::set_file_times(
        dst,
        FileTime::from_last_access_time(meta),
        FileTime::from_last_modification_time(meta),
    );
    let _ = fs::set_permissions(dst, meta.permissions());
}

#[cfg(unix)]
fn make_symlink(target: &Path, link: &Path) -> io::Result<()> {
    std::os::unix::fs::symlink(target, link)
}

#[cfg(not(unix))]
fn make_symlink(_target: &Path, _link: &Path) -> io::Result<()> {
    Err(io::Error::new(
        ErrorKind::Unsupported,
        "symlinks are not copied on this platform",
    ))
}
