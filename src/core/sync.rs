//! Directory synchronization: compare two trees, derive a default action per difference and
//! apply the (possibly edited) list.

use std::fs;
use std::io;
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::SystemTime;

use filetime::FileTime;

use crate::core::compare::files_identical;
use crate::core::wildcard::Mask;

#[derive(Clone, Debug)]
pub struct SyncOptions {
    /// Descend into subdirectories.
    pub subdirs: bool,
    /// Decide "equal" by comparing the bytes, not size and date.
    pub by_content: bool,
    /// Same size counts as equal whatever the dates say.
    pub ignore_date: bool,
    /// File-name mask (see `Mask`); directories are never filtered by it.
    pub mask: String,
    /// List directories that exist, empty, on one side only.
    pub empty_dirs: bool,
    /// The right side becomes a mirror of the left one.
    pub asymmetric: bool,
    /// Dates closer than this are equal (2 s: FAT stores even seconds).
    pub time_tolerance_secs: u64,
}

impl Default for SyncOptions {
    fn default() -> Self {
        SyncOptions {
            subdirs: true,
            by_content: false,
            ignore_date: false,
            mask: "*".to_string(),
            empty_dirs: true,
            asymmetric: false,
            time_tolerance_secs: 2,
        }
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum SyncStatus {
    Equal,
    LeftOnly,
    RightOnly,
    LeftNewer,
    RightNewer,
    /// Present on both sides, differing, with no date to tell which wins.
    Different,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum SyncAction {
    None,
    CopyToRight,
    CopyToLeft,
    DeleteRight,
    DeleteLeft,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct Side {
    pub size: u64,
    pub modified: Option<SystemTime>,
}

#[derive(Clone, PartialEq, Eq, Debug)]
pub struct SyncItem {
    /// Path relative to both roots.
    pub rel: PathBuf,
    pub is_dir: bool,
    pub left: Option<Side>,
    pub right: Option<Side>,
    pub status: SyncStatus,
    pub action: SyncAction,
}

mod scan;

use scan::{Entry, scan};

fn cancelled() -> io::Error {
    io::Error::new(io::ErrorKind::Interrupted, "cancelled")
}

/// Seconds between two optional dates, `Some(signed)` = left minus right.
fn date_delta(l: Option<SystemTime>, r: Option<SystemTime>) -> Option<i128> {
    let secs = |t: SystemTime| -> i128 {
        match t.duration_since(SystemTime::UNIX_EPOCH) {
            Ok(d) => d.as_secs() as i128,
            Err(e) => -(e.duration().as_secs() as i128),
        }
    };
    Some(secs(l?) - secs(r?))
}

fn classify(
    l: &Side,
    r: &Side,
    lpath: &Path,
    rpath: &Path,
    opts: &SyncOptions,
) -> io::Result<SyncStatus> {
    let delta = date_delta(l.modified, r.modified).unwrap_or(0);
    let same_date = delta.unsigned_abs() <= u128::from(opts.time_tolerance_secs);
    let same_content = if l.size != r.size {
        false
    } else if opts.by_content {
        files_identical(lpath, rpath)?
    } else {
        opts.ignore_date || same_date
    };
    if same_content {
        return Ok(SyncStatus::Equal);
    }
    Ok(if opts.ignore_date || same_date {
        SyncStatus::Different
    } else if delta > 0 {
        SyncStatus::LeftNewer
    } else {
        SyncStatus::RightNewer
    })
}

fn default_action(status: SyncStatus, asymmetric: bool) -> SyncAction {
    use SyncAction::*;
    use SyncStatus::*;
    match (status, asymmetric) {
        (Equal, _) => None,
        (LeftOnly | LeftNewer, _) => CopyToRight,
        (RightOnly, false) | (RightNewer, false) => CopyToLeft,
        (RightOnly, true) => DeleteRight,
        (RightNewer | Different, true) => CopyToRight,
        (Different, false) => None,
    }
}

/// A file facing a directory of the same name: only the file is listed, on its own side.
fn resolve_clash<'a>(
    l: Option<&'a Entry>,
    r: Option<&'a Entry>,
) -> (Option<&'a Entry>, Option<&'a Entry>) {
    match (l, r) {
        (Some(a), Some(b)) if a.is_dir && !b.is_dir => (None, Some(b)),
        (Some(a), Some(b)) if !a.is_dir && b.is_dir => (Some(a), None),
        pair => pair,
    }
}

/// Status and kind of the item to list for `rel`; `None` when it is not listed (a directory
/// on both sides, or a one-sided directory that is not empty).
fn item_status(
    l: Option<&Entry>,
    r: Option<&Entry>,
    rel: &Path,
    (left, right): (&Path, &Path),
    opts: &SyncOptions,
) -> io::Result<Option<(SyncStatus, bool)>> {
    Ok(match (l, r) {
        (Some(a), Some(b)) if a.is_dir && b.is_dir => None,
        (Some(a), Some(b)) => {
            let status = classify(&a.side, &b.side, &left.join(rel), &right.join(rel), opts)?;
            Some((status, false))
        }
        (Some(a), None) if a.is_dir && !a.empty => None,
        (Some(a), None) => Some((SyncStatus::LeftOnly, a.is_dir)),
        (None, Some(b)) if b.is_dir && !b.empty => None,
        (None, Some(b)) => Some((SyncStatus::RightOnly, b.is_dir)),
        (None, None) => None,
    })
}

/// Compare two directory trees. Result sorted by relative path (a directory before its
/// children). Default actions:
/// - symmetric: `LeftOnly`/`LeftNewer` copy right, `RightOnly`/`RightNewer` copy left,
///   `Different` does nothing;
/// - asymmetric: right mirrors left (`RightOnly` deletes, `RightNewer`/`Different` copy right).
///
/// Directories appear only with `empty_dirs`, when empty and missing on the other side. A file
/// facing a directory of the same name is listed as one-sided (the file's side). Returns
/// `ErrorKind::Interrupted` when cancelled and any I/O error met while scanning.
pub fn compare_dirs(
    left: &Path,
    right: &Path,
    opts: &SyncOptions,
    cancel: &AtomicBool,
) -> io::Result<Vec<SyncItem>> {
    let mask = Mask::new(&opts.mask);
    let lmap = scan(left, opts, &mask, cancel)?;
    let rmap = scan(right, opts, &mask, cancel)?;
    let mut rels: Vec<&PathBuf> = lmap.keys().chain(rmap.keys()).collect();
    rels.sort();
    rels.dedup();

    let mut items = Vec::new();
    for rel in rels {
        if cancel.load(Ordering::Relaxed) {
            return Err(cancelled());
        }
        let (l, r) = resolve_clash(lmap.get(rel), rmap.get(rel));
        let Some((status, is_dir)) = item_status(l, r, rel, (left, right), opts)? else {
            continue;
        };
        items.push(SyncItem {
            rel: rel.clone(),
            is_dir,
            left: l.map(|e| e.side),
            right: r.map(|e| e.side),
            status,
            action: default_action(status, opts.asymmetric),
        });
    }
    Ok(items)
}

/// What `apply_sync` did.
#[derive(Clone, Debug, Default)]
pub struct SyncReport {
    pub copied: usize,
    pub deleted: usize,
    pub errors: Vec<String>,
}

/// A relative path made only of plain names: never absolute, never `..`.
fn is_safe_rel(rel: &Path) -> bool {
    !rel.as_os_str().is_empty() && rel.components().all(|c| matches!(c, Component::Normal(_)))
}

fn copy_item(src: &Path, dst: &Path, is_dir: bool) -> io::Result<()> {
    if is_dir {
        fs::create_dir_all(dst)?;
        return Ok(());
    }
    if let Some(parent) = dst.parent() {
        fs::create_dir_all(parent)?;
    }
    fs::copy(src, dst)?;
    let mtime = FileTime::from_last_modification_time(&fs::metadata(src)?);
    filetime::set_file_mtime(dst, mtime)
}

fn delete_item(path: &Path, is_dir: bool) -> io::Result<()> {
    if is_dir {
        // Only empty directories are ever listed: never remove a subtree here.
        fs::remove_dir(path)
    } else {
        fs::remove_file(path)
    }
}

/// Apply the actions of `items` in order. `progress` receives each item's relative path before
/// it runs; returning `false` (or setting `cancel`) stops. Errors are collected, not fatal.
/// Copies preserve the modification time. Deleting a directory removes it only when empty.
pub fn apply_sync(
    left: &Path,
    right: &Path,
    items: &[SyncItem],
    cancel: &AtomicBool,
    progress: &mut dyn FnMut(&Path) -> bool,
) -> SyncReport {
    let mut report = SyncReport::default();
    for item in items {
        if item.action == SyncAction::None {
            continue;
        }
        if cancel.load(Ordering::Relaxed) || !progress(&item.rel) {
            break;
        }
        if !is_safe_rel(&item.rel) {
            report
                .errors
                .push(format!("unsafe path: {}", item.rel.display()));
            continue;
        }
        let (l, r) = (left.join(&item.rel), right.join(&item.rel));
        let (result, deleted, shown) = match item.action {
            SyncAction::CopyToRight => (copy_item(&l, &r, item.is_dir), false, &r),
            SyncAction::CopyToLeft => (copy_item(&r, &l, item.is_dir), false, &l),
            SyncAction::DeleteRight => (delete_item(&r, item.is_dir), true, &r),
            SyncAction::DeleteLeft => (delete_item(&l, item.is_dir), true, &l),
            SyncAction::None => continue,
        };
        match result {
            Ok(()) if deleted => report.deleted += 1,
            Ok(()) => report.copied += 1,
            Err(e) => report.errors.push(format!("{}: {e}", shown.display())),
        }
    }
    report
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    fn at(secs: u64) -> SystemTime {
        SystemTime::UNIX_EPOCH + Duration::from_secs(1_700_000_000 + secs)
    }

    fn put(root: &Path, rel: &str, content: &str, secs: u64) {
        let p = root.join(rel);
        fs::create_dir_all(p.parent().unwrap()).unwrap();
        fs::write(&p, content).unwrap();
        filetime::set_file_mtime(&p, FileTime::from_system_time(at(secs))).unwrap();
    }

    fn run(l: &Path, r: &Path, opts: &SyncOptions) -> Vec<SyncItem> {
        compare_dirs(l, r, opts, &AtomicBool::new(false)).unwrap()
    }

    fn find<'a>(items: &'a [SyncItem], rel: &str) -> &'a SyncItem {
        items
            .iter()
            .find(|i| i.rel == Path::new(rel))
            .unwrap_or_else(|| panic!("{rel} not listed in {items:?}"))
    }

    fn dirs() -> (tempfile::TempDir, tempfile::TempDir) {
        (tempfile::tempdir().unwrap(), tempfile::tempdir().unwrap())
    }

    #[test]
    fn every_status_with_nested_dirs() {
        let (l, r) = dirs();
        put(l.path(), "same.txt", "x", 10);
        put(r.path(), "same.txt", "x", 10);
        put(l.path(), "lonely.txt", "x", 10);
        put(r.path(), "orphan.txt", "x", 10);
        put(l.path(), "new_left.txt", "aa", 100);
        put(r.path(), "new_left.txt", "bb", 10);
        put(l.path(), "sub/new_right.txt", "aa", 10);
        put(r.path(), "sub/new_right.txt", "bb", 100);
        put(l.path(), "sub/deep/diff.txt", "short", 10);
        put(r.path(), "sub/deep/diff.txt", "longer text", 10);
        put(l.path(), "sub/deep/only.txt", "x", 10);

        let items = run(l.path(), r.path(), &SyncOptions::default());
        use SyncAction::*;
        use SyncStatus::*;
        let expect = [
            ("same.txt", Equal, None),
            ("lonely.txt", LeftOnly, CopyToRight),
            ("orphan.txt", RightOnly, CopyToLeft),
            ("new_left.txt", LeftNewer, CopyToRight),
            ("sub/new_right.txt", RightNewer, CopyToLeft),
            ("sub/deep/diff.txt", Different, None),
            ("sub/deep/only.txt", LeftOnly, CopyToRight),
        ];
        assert_eq!(items.len(), expect.len(), "{items:?}");
        for (rel, status, action) in expect {
            let it = find(&items, rel);
            assert_eq!(
                (it.status, it.action, it.is_dir),
                (status, action, false),
                "{rel}"
            );
        }
        let it = find(&items, "new_left.txt");
        assert_eq!(it.left.unwrap().size, 2);
        assert_eq!(it.right.unwrap().modified, Some(at(10)));
        assert!(find(&items, "lonely.txt").right.is_none());
        // sorted by relative path
        let rels: Vec<&PathBuf> = items.iter().map(|i| &i.rel).collect();
        assert!(rels.windows(2).all(|w| w[0] <= w[1]));
    }

    #[test]
    fn time_tolerance() {
        let (l, r) = dirs();
        put(l.path(), "f", "x", 11);
        put(r.path(), "f", "x", 10);
        let items = run(l.path(), r.path(), &SyncOptions::default());
        assert_eq!(items[0].status, SyncStatus::Equal);
        let strict = SyncOptions {
            time_tolerance_secs: 0,
            ..Default::default()
        };
        assert_eq!(
            run(l.path(), r.path(), &strict)[0].status,
            SyncStatus::LeftNewer
        );
    }

    #[test]
    fn no_subdirs() {
        let (l, r) = dirs();
        put(l.path(), "top.txt", "x", 10);
        put(l.path(), "sub/inner.txt", "x", 10);
        let opts = SyncOptions {
            subdirs: false,
            ..Default::default()
        };
        let items = run(l.path(), r.path(), &opts);
        assert_eq!(items.len(), 1);
        assert_eq!(items[0].rel, Path::new("top.txt"));
    }

    #[test]
    fn mask_filters_files() {
        let (l, r) = dirs();
        put(l.path(), "a.txt", "x", 10);
        put(l.path(), "b.bin", "x", 10);
        put(l.path(), "sub/c.txt", "x", 10);
        put(l.path(), "sub/d.bak", "x", 10);
        let opts = SyncOptions {
            mask: "*.txt".into(),
            ..Default::default()
        };
        let rels: Vec<_> = run(l.path(), r.path(), &opts)
            .into_iter()
            .map(|i| i.rel)
            .collect();
        assert_eq!(rels, [PathBuf::from("a.txt"), PathBuf::from("sub/c.txt")]);
        let opts = SyncOptions {
            mask: "* | *.bak;*.bin".into(),
            ..Default::default()
        };
        assert_eq!(run(l.path(), r.path(), &opts).len(), 2);
    }

    #[test]
    fn by_content() {
        let (l, r) = dirs();
        // same bytes, different dates: a date mismatch unless comparing content
        put(l.path(), "moved.txt", "same", 100);
        put(r.path(), "moved.txt", "same", 10);
        // same size and date, different bytes: equal by size/date, different by content
        put(l.path(), "sneaky.txt", "aaaa", 10);
        put(r.path(), "sneaky.txt", "bbbb", 10);

        let plain = run(l.path(), r.path(), &SyncOptions::default());
        assert_eq!(find(&plain, "moved.txt").status, SyncStatus::LeftNewer);
        assert_eq!(find(&plain, "sneaky.txt").status, SyncStatus::Equal);

        let opts = SyncOptions {
            by_content: true,
            ..Default::default()
        };
        let items = run(l.path(), r.path(), &opts);
        // Equal items are still listed, with no action
        assert_eq!(find(&items, "moved.txt").status, SyncStatus::Equal);
        assert_eq!(find(&items, "moved.txt").action, SyncAction::None);
        assert_eq!(find(&items, "sneaky.txt").status, SyncStatus::Different);
    }

    #[test]
    fn ignore_date() {
        let (l, r) = dirs();
        put(l.path(), "a", "same", 100);
        put(r.path(), "a", "same", 10);
        put(l.path(), "b", "one", 100);
        put(r.path(), "b", "three", 10);
        let opts = SyncOptions {
            ignore_date: true,
            ..Default::default()
        };
        let items = run(l.path(), r.path(), &opts);
        assert_eq!(find(&items, "a").status, SyncStatus::Equal);
        assert_eq!(find(&items, "b").status, SyncStatus::Different);
    }

    #[test]
    fn empty_dirs() {
        let (l, r) = dirs();
        fs::create_dir_all(l.path().join("hollow/inner")).unwrap();
        fs::create_dir_all(r.path().join("rhollow")).unwrap();
        put(l.path(), "full/f.txt", "x", 10);
        fs::create_dir_all(l.path().join("both")).unwrap();
        fs::create_dir_all(r.path().join("both")).unwrap();

        let items = run(l.path(), r.path(), &SyncOptions::default());
        let rels: Vec<_> = items
            .iter()
            .map(|i| i.rel.to_string_lossy().into_owned())
            .collect();
        assert_eq!(rels, ["full/f.txt", "hollow/inner", "rhollow"]);
        let it = find(&items, "hollow/inner");
        assert!(it.is_dir);
        assert_eq!(
            (it.status, it.action),
            (SyncStatus::LeftOnly, SyncAction::CopyToRight)
        );
        assert!(it.right.is_none());
        let it = find(&items, "rhollow");
        assert_eq!(
            (it.status, it.action),
            (SyncStatus::RightOnly, SyncAction::CopyToLeft)
        );

        let off = SyncOptions {
            empty_dirs: false,
            ..Default::default()
        };
        let rels: Vec<_> = run(l.path(), r.path(), &off)
            .into_iter()
            .map(|i| i.rel)
            .collect();
        assert_eq!(rels, [PathBuf::from("full/f.txt")]);
    }

    #[test]
    fn asymmetric_default_actions() {
        let (l, r) = dirs();
        put(l.path(), "left_only", "x", 10);
        put(r.path(), "right_only", "x", 10);
        put(l.path(), "left_newer", "aa", 100);
        put(r.path(), "left_newer", "bb", 10);
        put(l.path(), "right_newer", "aa", 10);
        put(r.path(), "right_newer", "bb", 100);
        put(l.path(), "different", "a", 10);
        put(r.path(), "different", "bbb", 10);
        put(l.path(), "equal", "x", 10);
        put(r.path(), "equal", "x", 10);
        fs::create_dir_all(r.path().join("rdir")).unwrap();

        let opts = SyncOptions {
            asymmetric: true,
            ..Default::default()
        };
        let items = run(l.path(), r.path(), &opts);
        use SyncAction::*;
        let expect = [
            ("left_only", CopyToRight),
            ("right_only", DeleteRight),
            ("left_newer", CopyToRight),
            ("right_newer", CopyToRight),
            ("different", CopyToRight),
            ("equal", None),
            ("rdir", DeleteRight),
        ];
        for (rel, action) in expect {
            assert_eq!(find(&items, rel).action, action, "{rel}");
        }
    }

    #[test]
    fn cancel_and_bad_root() {
        let (l, r) = dirs();
        put(l.path(), "a", "x", 10);
        let stop = AtomicBool::new(true);
        let err = compare_dirs(l.path(), r.path(), &SyncOptions::default(), &stop).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Interrupted);
        let missing = l.path().join("nope");
        assert!(
            compare_dirs(
                &missing,
                r.path(),
                &SyncOptions::default(),
                &AtomicBool::new(false)
            )
            .is_err()
        );
        let file = l.path().join("a");
        assert!(
            compare_dirs(
                &file,
                r.path(),
                &SyncOptions::default(),
                &AtomicBool::new(false)
            )
            .is_err()
        );
    }

    #[test]
    fn file_facing_directory_lists_the_file_only() {
        let (l, r) = dirs();
        put(l.path(), "x", "file", 10);
        put(r.path(), "x/inner.txt", "y", 10);
        let items = run(l.path(), r.path(), &SyncOptions::default());
        let x = find(&items, "x");
        assert!(!x.is_dir);
        assert_eq!(x.status, SyncStatus::LeftOnly);
        assert!(x.right.is_none());
    }

    #[test]
    fn apply_copies_both_ways_preserving_mtime() {
        let (l, r) = dirs();
        put(l.path(), "to_right.txt", "from left", 500);
        put(l.path(), "sub/nested.txt", "nested", 600);
        put(r.path(), "to_left.txt", "from right", 700);
        fs::create_dir_all(l.path().join("empty/deeper")).unwrap();
        let items = run(l.path(), r.path(), &SyncOptions::default());

        let mut seen = Vec::new();
        let report = apply_sync(
            l.path(),
            r.path(),
            &items,
            &AtomicBool::new(false),
            &mut |p| {
                seen.push(p.to_path_buf());
                true
            },
        );
        assert!(report.errors.is_empty(), "{:?}", report.errors);
        assert_eq!((report.copied, report.deleted), (4, 0));
        assert_eq!(seen.len(), 4);
        assert_eq!(
            fs::read_to_string(r.path().join("to_right.txt")).unwrap(),
            "from left"
        );
        assert_eq!(
            fs::read_to_string(r.path().join("sub/nested.txt")).unwrap(),
            "nested"
        );
        assert_eq!(
            fs::read_to_string(l.path().join("to_left.txt")).unwrap(),
            "from right"
        );
        assert!(r.path().join("empty/deeper").is_dir());
        let mtime = |p: PathBuf| fs::metadata(p).unwrap().modified().unwrap();
        assert_eq!(mtime(r.path().join("to_right.txt")), at(500));
        assert_eq!(mtime(r.path().join("sub/nested.txt")), at(600));
        assert_eq!(mtime(l.path().join("to_left.txt")), at(700));

        // a second pass finds nothing left to do
        let again = run(l.path(), r.path(), &SyncOptions::default());
        assert!(
            again.iter().all(|i| i.status == SyncStatus::Equal),
            "{again:?}"
        );
    }

    #[test]
    fn apply_overwrites_the_older_file() {
        let (l, r) = dirs();
        put(l.path(), "f.txt", "old", 10);
        put(r.path(), "f.txt", "newer!", 100);
        let items = run(l.path(), r.path(), &SyncOptions::default());
        assert_eq!(items[0].action, SyncAction::CopyToLeft);
        let report = apply_sync(
            l.path(),
            r.path(),
            &items,
            &AtomicBool::new(false),
            &mut |_| true,
        );
        assert_eq!(report.copied, 1);
        assert_eq!(
            fs::read_to_string(l.path().join("f.txt")).unwrap(),
            "newer!"
        );
    }

    #[test]
    fn apply_deletes_in_asymmetric_mode() {
        let (l, r) = dirs();
        put(l.path(), "keep.txt", "k", 10);
        put(r.path(), "stale.txt", "s", 10);
        put(r.path(), "sub/stale2.txt", "s", 10);
        fs::create_dir_all(r.path().join("rempty")).unwrap();
        let opts = SyncOptions {
            asymmetric: true,
            ..Default::default()
        };
        let items = run(l.path(), r.path(), &opts);
        let report = apply_sync(
            l.path(),
            r.path(),
            &items,
            &AtomicBool::new(false),
            &mut |_| true,
        );
        assert!(report.errors.is_empty(), "{:?}", report.errors);
        assert_eq!((report.copied, report.deleted), (1, 3));
        assert!(r.path().join("keep.txt").is_file());
        assert!(!r.path().join("stale.txt").exists());
        assert!(!r.path().join("sub/stale2.txt").exists());
        assert!(!r.path().join("rempty").exists());
        assert!(l.path().join("keep.txt").is_file());
    }

    #[test]
    fn apply_honours_edited_actions_cancel_and_progress() {
        let (l, r) = dirs();
        put(l.path(), "a.txt", "a", 10);
        put(l.path(), "b.txt", "b", 10);
        let mut items = run(l.path(), r.path(), &SyncOptions::default());
        // the user flips one to a deletion on the left and skips nothing else
        items[0].action = SyncAction::DeleteLeft;
        let report = apply_sync(
            l.path(),
            r.path(),
            &items,
            &AtomicBool::new(false),
            &mut |_| true,
        );
        assert_eq!((report.copied, report.deleted), (1, 1));
        assert!(!l.path().join("a.txt").exists());
        assert!(!r.path().join("a.txt").exists());
        assert!(r.path().join("b.txt").exists());

        // progress returning false stops before the item runs
        let (l, r) = dirs();
        put(l.path(), "a.txt", "a", 10);
        let items = run(l.path(), r.path(), &SyncOptions::default());
        let report = apply_sync(
            l.path(),
            r.path(),
            &items,
            &AtomicBool::new(false),
            &mut |_| false,
        );
        assert_eq!(report.copied, 0);
        assert!(!r.path().join("a.txt").exists());
        // so does the cancel flag
        let report = apply_sync(
            l.path(),
            r.path(),
            &items,
            &AtomicBool::new(true),
            &mut |_| true,
        );
        assert_eq!(report.copied, 0);
    }

    #[test]
    fn apply_reports_errors_and_rejects_unsafe_paths() {
        let (l, r) = dirs();
        let side = Some(Side {
            size: 1,
            modified: None,
        });
        let item = |rel: &str, action| SyncItem {
            rel: PathBuf::from(rel),
            is_dir: false,
            left: side,
            right: None,
            status: SyncStatus::LeftOnly,
            action,
        };
        let items = [
            item("../escape.txt", SyncAction::CopyToRight),
            item("missing.txt", SyncAction::CopyToRight),
        ];
        let report = apply_sync(
            l.path(),
            r.path(),
            &items,
            &AtomicBool::new(false),
            &mut |_| true,
        );
        assert_eq!((report.copied, report.deleted), (0, 0));
        assert_eq!(report.errors.len(), 2, "{:?}", report.errors);
        assert!(report.errors[0].contains("unsafe"));
    }
}
