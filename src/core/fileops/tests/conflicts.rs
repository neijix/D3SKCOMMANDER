use super::*;
use tempfile::tempdir;

/// `src/a.txt` ("source", mtime `src_time`) against `out/a.txt` ("target", `dst_time`).
pub(super) fn conflict_setup(tmp: &Path, src_time: i64, dst_time: i64) -> (PathBuf, PathBuf) {
    let src = tmp.join("src/a.txt");
    write(&src, "source");
    set_mtime(&src, src_time);
    let dest = tmp.join("out");
    write(&dest.join("a.txt"), "target");
    set_mtime(&dest.join("a.txt"), dst_time);
    (src, dest)
}

#[test]
fn conflict_event_describes_both_files() {
    let tmp = tempdir().unwrap();
    let (src, dest) = conflict_setup(tmp.path(), NEW, OLD);

    let out = go_with(
        &request(OpKind::Copy, &[&src], Some(&dest)),
        ConflictChoice::Skip,
    );

    let (source, target) = &out.conflicts[0];
    assert_eq!((source.size, target.size), (6, 6));
    assert_eq!(source.path.file_name(), src.file_name());
    assert_eq!(target.path, dest.canonicalize().unwrap().join("a.txt"));
    assert!(source.modified > target.modified);
}

#[test]
fn conflict_overwrite() {
    let tmp = tempdir().unwrap();
    let (src, dest) = conflict_setup(tmp.path(), OLD, NEW);

    let out = go_with(
        &request(OpKind::Copy, &[&src], Some(&dest)),
        ConflictChoice::Overwrite,
    );

    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "source");
}

#[test]
fn conflict_skip() {
    let tmp = tempdir().unwrap();
    let (src, dest) = conflict_setup(tmp.path(), NEW, OLD);

    let out = go_with(
        &request(OpKind::Copy, &[&src], Some(&dest)),
        ConflictChoice::Skip,
    );

    assert_eq!(out.finished(), (0, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "target");
    let last = out.last_progress();
    assert_eq!(
        (last.done_files, last.done_bytes),
        (1, 6),
        "a skip fills the bar"
    );
}

/// Two conflicting files `a.txt` and `b.txt` in one operation.
fn two_conflicts(tmp: &Path, src_time: i64, dst_time: i64) -> (OpRequest, PathBuf) {
    let dest = tmp.join("out");
    let mut sources = Vec::new();
    for name in ["a.txt", "b.txt"] {
        let src = tmp.join("src").join(name);
        write(&src, "source");
        set_mtime(&src, src_time);
        write(&dest.join(name), "target");
        set_mtime(&dest.join(name), dst_time);
        sources.push(src);
    }
    let refs: Vec<&Path> = sources.iter().map(PathBuf::as_path).collect();
    (request(OpKind::Copy, &refs, Some(&dest)), dest)
}

#[test]
fn conflict_overwrite_all_asks_once() {
    let tmp = tempdir().unwrap();
    let (req, dest) = two_conflicts(tmp.path(), OLD, NEW);

    let out = go_with(&req, ConflictChoice::OverwriteAll);

    assert_eq!(out.conflicts.len(), 1);
    assert_eq!(out.finished(), (2, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "source");
    assert_eq!(read(&dest.join("b.txt")), "source");
}

#[test]
fn conflict_skip_all_asks_once() {
    let tmp = tempdir().unwrap();
    let (req, dest) = two_conflicts(tmp.path(), NEW, OLD);

    let out = go_with(&req, ConflictChoice::SkipAll);

    assert_eq!(out.conflicts.len(), 1);
    assert_eq!(out.finished(), (0, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "target");
    assert_eq!(read(&dest.join("b.txt")), "target");
}

#[test]
fn conflict_overwrite_all_older_replaces_only_older_targets() {
    let tmp = tempdir().unwrap();
    let dest = tmp.path().join("out");
    let a = tmp.path().join("src/a.txt");
    let b = tmp.path().join("src/b.txt");
    write(&a, "source");
    set_mtime(&a, NEW);
    write(&dest.join("a.txt"), "target"); // older than its source: replaced
    set_mtime(&dest.join("a.txt"), OLD);
    write(&b, "source");
    set_mtime(&b, OLD);
    write(&dest.join("b.txt"), "target"); // newer than its source: kept
    set_mtime(&dest.join("b.txt"), NEW);

    let out = go_with(
        &request(OpKind::Copy, &[&a, &b], Some(&dest)),
        ConflictChoice::OverwriteAllOlder,
    );

    assert_eq!(out.conflicts.len(), 1);
    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "source");
    assert_eq!(read(&dest.join("b.txt")), "target");
}

#[test]
fn conflict_rename_new_writes_a_unique_name() {
    let tmp = tempdir().unwrap();
    let (src, dest) = conflict_setup(tmp.path(), OLD, NEW);
    write(&dest.join("a (2).txt"), "taken");

    let out = go_with(
        &request(OpKind::Copy, &[&src], Some(&dest)),
        ConflictChoice::RenameNew,
    );

    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "target");
    assert_eq!(read(&dest.join("a (2).txt")), "taken");
    assert_eq!(read(&dest.join("a (3).txt")), "source");
}

#[test]
fn conflict_cancel_stops_the_operation() {
    let tmp = tempdir().unwrap();
    let (req, dest) = two_conflicts(tmp.path(), OLD, NEW);

    let out = go_with(&req, ConflictChoice::Cancel);

    assert_eq!(out.finished(), (0, 0, true));
    assert_eq!(read(&dest.join("a.txt")), "target");
    assert_eq!(out.conflicts.len(), 1, "nothing is asked after Cancel");
}

#[test]
fn a_dropped_reply_cancels() {
    let tmp = tempdir().unwrap();
    let (src, dest) = conflict_setup(tmp.path(), OLD, NEW);

    let out = drive(
        &request(OpKind::Copy, &[&src], Some(&dest)),
        &AtomicBool::new(false),
        |_, _| None,
    );

    assert_eq!(out.finished(), (0, 0, true));
    assert_eq!(read(&dest.join("a.txt")), "target");
}
