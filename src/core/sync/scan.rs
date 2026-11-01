//! Tree scan for [`super::compare_dirs`]: one side's entries keyed by relative path.

use std::collections::BTreeMap;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

use walkdir::WalkDir;

use super::{Side, SyncOptions, cancelled};
use crate::core::wildcard::Mask;

/// One scanned file or directory.
pub(super) struct Entry {
    pub(super) is_dir: bool,
    pub(super) side: Side,
    /// Directories only, and only computed when `empty_dirs` is on.
    pub(super) empty: bool,
}

/// Every entry under `root` keyed by its relative path. Any unreadable directory is an error:
/// a silently missing subtree would be offered for deletion on the other side.
pub(super) fn scan(
    root: &Path,
    opts: &SyncOptions,
    mask: &Mask,
    cancel: &AtomicBool,
) -> io::Result<BTreeMap<PathBuf, Entry>> {
    if !fs::metadata(root)?.is_dir() {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            format!("not a directory: {}", root.display()),
        ));
    }
    let max_depth = if opts.subdirs { usize::MAX } else { 1 };
    let mut out = BTreeMap::new();
    for entry in WalkDir::new(root).min_depth(1).max_depth(max_depth) {
        if cancel.load(Ordering::Relaxed) {
            return Err(cancelled());
        }
        let entry = entry?;
        // A symlink counts as what it points to; a dangling one is skipped.
        let meta = if entry.path_is_symlink() {
            match fs::metadata(entry.path()) {
                Ok(m) if m.is_file() => m,
                _ => continue,
            }
        } else {
            entry.metadata()?
        };
        let is_dir = meta.is_dir();
        if !is_dir && !mask.matches(&entry.file_name().to_string_lossy()) {
            continue;
        }
        let Ok(rel) = entry.path().strip_prefix(root) else {
            continue;
        };
        let empty = is_dir && opts.empty_dirs && fs::read_dir(entry.path())?.next().is_none();
        out.insert(
            rel.to_path_buf(),
            Entry {
                is_dir,
                side: Side {
                    size: if is_dir { 0 } else { meta.len() },
                    modified: meta.modified().ok(),
                },
                empty,
            },
        );
    }
    Ok(out)
}
