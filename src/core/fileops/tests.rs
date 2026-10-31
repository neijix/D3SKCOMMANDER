use super::transfer::BUFFER_SIZE;
use super::*;
use filetime::FileTime;
use tempfile::tempdir;

mod conflicts;
use conflicts::conflict_setup;

/// Everything a run reported; conflicts are summarized so a dropped reply really drops.
pub(super) struct Outcome {
    pub(super) events: Vec<OpEvent>,
    pub(super) conflicts: Vec<(FileInfo, FileInfo)>,
}

impl Outcome {
    pub(super) fn finished(&self) -> (u64, u64, bool) {
        match self.events.last() {
            Some(OpEvent::Finished {
                done_files,
                errors,
                cancelled,
            }) => (*done_files, *errors, *cancelled),
            other => panic!("the last event must be Finished, got {other:?}"),
        }
    }

    pub(super) fn errors(&self) -> Vec<String> {
        self.events
            .iter()
            .filter_map(|e| match e {
                OpEvent::Error { message, .. } => Some(message.clone()),
                _ => None,
            })
            .collect()
    }

    pub(super) fn last_progress(&self) -> OpProgress {
        self.events
            .iter()
            .rev()
            .find_map(|e| match e {
                OpEvent::Progress(p) => Some(p.clone()),
                _ => None,
            })
            .expect("a Progress event")
    }
}

/// Drives `run`; `answer` replies to each conflict (`None` drops the reply channel).
pub(super) fn drive(
    req: &OpRequest,
    cancel: &AtomicBool,
    mut answer: impl FnMut(&FileInfo, &FileInfo) -> Option<ConflictChoice>,
) -> Outcome {
    let mut out = Outcome {
        events: Vec::new(),
        conflicts: Vec::new(),
    };
    run(req, cancel, &mut |event| match event {
        OpEvent::Conflict {
            source,
            target,
            reply,
        } => {
            if let Some(choice) = answer(&source, &target) {
                reply.send(choice).expect("the engine waits for the reply");
            }
            out.conflicts.push((source, target));
        }
        other => out.events.push(other),
    });
    out
}

pub(super) fn go(req: &OpRequest) -> Outcome {
    drive(req, &AtomicBool::new(false), |_, _| {
        panic!("unexpected conflict")
    })
}

pub(super) fn go_with(req: &OpRequest, choice: ConflictChoice) -> Outcome {
    drive(req, &AtomicBool::new(false), |_, _| Some(choice))
}

pub(super) fn request(kind: OpKind, sources: &[&Path], dest: Option<&Path>) -> OpRequest {
    OpRequest {
        kind,
        sources: sources.iter().map(|p| p.to_path_buf()).collect(),
        dest_dir: dest.map(Path::to_path_buf),
        rename_to: None,
    }
}

pub(super) fn write(path: &Path, text: &str) {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).unwrap();
    }
    fs::write(path, text).unwrap();
}

pub(super) fn read(path: &Path) -> String {
    fs::read_to_string(path).unwrap()
}

pub(super) fn set_mtime(path: &Path, unix_secs: i64) {
    filetime::set_file_mtime(path, FileTime::from_unix_time(unix_secs, 0)).unwrap();
}

pub(super) fn mtime(path: &Path) -> FileTime {
    FileTime::from_last_modification_time(&fs::metadata(path).unwrap())
}

pub(super) const OLD: i64 = 1_500_000_000;
pub(super) const NEW: i64 = 1_700_000_000;

#[test]
fn copy_file_keeps_content_and_mtime() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "hello");
    set_mtime(&src, OLD);
    let dest = tmp.path().join("out");

    let out = go(&request(OpKind::Copy, &[&src], Some(&dest)));

    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "hello");
    assert_eq!(mtime(&dest.join("a.txt")), FileTime::from_unix_time(OLD, 0));
    assert!(src.exists(), "a copy keeps the source");
}

#[cfg(unix)]
#[test]
fn copy_file_keeps_permissions() {
    use std::os::unix::fs::PermissionsExt;
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("run.sh");
    write(&src, "#!/bin/sh\n");
    fs::set_permissions(&src, fs::Permissions::from_mode(0o751)).unwrap();
    let dest = tmp.path().join("out");

    go(&request(OpKind::Copy, &[&src], Some(&dest)));

    let mode = fs::metadata(dest.join("run.sh"))
        .unwrap()
        .permissions()
        .mode();
    assert_eq!(mode & 0o777, 0o751);
}

#[test]
fn copy_tree_with_empty_dirs_and_totals() {
    let tmp = tempdir().unwrap();
    let tree = tmp.path().join("tree");
    write(&tree.join("top.txt"), "12345");
    write(&tree.join("sub/deep/leaf.txt"), "abc");
    fs::create_dir_all(tree.join("empty")).unwrap();
    set_mtime(&tree.join("sub/deep/leaf.txt"), OLD);
    let dest = tmp.path().join("out");

    let out = go(&request(OpKind::Copy, &[&tree], Some(&dest)));

    assert_eq!(out.finished(), (2, 0, false));
    assert_eq!(read(&dest.join("tree/top.txt")), "12345");
    assert_eq!(read(&dest.join("tree/sub/deep/leaf.txt")), "abc");
    assert!(dest.join("tree/empty").is_dir());
    assert_eq!(
        mtime(&dest.join("tree/sub/deep/leaf.txt")),
        FileTime::from_unix_time(OLD, 0)
    );
    let last = out.last_progress();
    assert_eq!((last.total_files, last.done_files), (2, 2));
    assert_eq!((last.total_bytes, last.done_bytes), (8, 8));
}

#[test]
fn copy_a_large_file_in_several_chunks() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("big.bin");
    let data: Vec<u8> = (0..(2 * BUFFER_SIZE + 123))
        .map(|i| (i % 251) as u8)
        .collect();
    fs::write(&src, &data).unwrap();
    let dest = tmp.path().join("out");

    let out = go(&request(OpKind::Copy, &[&src], Some(&dest)));

    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(fs::read(dest.join("big.bin")).unwrap(), data);
    assert_eq!(out.last_progress().done_bytes, data.len() as u64);
}

#[test]
fn copy_with_rename_to() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "x");
    let dest = tmp.path().join("out");
    let mut req = request(OpKind::Copy, &[&src], Some(&dest));
    req.rename_to = Some("b.txt".into());

    let out = go(&req);

    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&dest.join("b.txt")), "x");
    assert!(!dest.join("a.txt").exists());
}

#[test]
fn rename_to_with_a_separator_is_refused() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "x");
    let dest = tmp.path().join("out");
    let mut req = request(OpKind::Copy, &[&src], Some(&dest));
    req.rename_to = Some("../evil.txt".into());

    let out = go(&req);

    assert_eq!(out.finished(), (0, 1, false));
    assert!(!tmp.path().join("evil.txt").exists());
}

#[test]
fn copy_without_a_destination_is_an_error() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "x");

    let out = go(&request(OpKind::Copy, &[&src], None));

    assert_eq!(out.finished(), (0, 1, false));
    assert!(out.errors()[0].contains("destination"));
}

#[test]
fn missing_source_reports_an_error_and_goes_on() {
    let tmp = tempdir().unwrap();
    let good = tmp.path().join("good.txt");
    write(&good, "ok");
    let dest = tmp.path().join("out");

    let out = go(&request(
        OpKind::Copy,
        &[&tmp.path().join("nope.txt"), &good],
        Some(&dest),
    ));

    assert_eq!(out.finished(), (1, 1, false));
    assert_eq!(read(&dest.join("good.txt")), "ok");
}

#[test]
fn move_on_the_same_device_renames() {
    let tmp = tempdir().unwrap();
    let file = tmp.path().join("a.txt");
    write(&file, "moved");
    set_mtime(&file, OLD);
    let tree = tmp.path().join("tree");
    write(&tree.join("sub/b.txt"), "bb");
    let dest = tmp.path().join("out");

    let out = go(&request(OpKind::Move, &[&file, &tree], Some(&dest)));

    assert_eq!(out.finished(), (2, 0, false));
    assert!(!file.exists() && !tree.exists());
    assert_eq!(read(&dest.join("a.txt")), "moved");
    assert_eq!(mtime(&dest.join("a.txt")), FileTime::from_unix_time(OLD, 0));
    assert_eq!(read(&dest.join("tree/sub/b.txt")), "bb");
    let last = out.last_progress();
    assert_eq!((last.total_files, last.done_files), (2, 2));
}

#[test]
fn move_onto_an_existing_folder_merges_and_asks_per_file() {
    let tmp = tempdir().unwrap();
    let tree = tmp.path().join("tree");
    write(&tree.join("new.txt"), "new");
    write(&tree.join("same.txt"), "from source");
    let dest = tmp.path().join("out");
    write(&dest.join("tree/same.txt"), "from target");
    write(&dest.join("tree/other.txt"), "other");

    let out = go_with(
        &request(OpKind::Move, &[&tree], Some(&dest)),
        ConflictChoice::Skip,
    );

    assert_eq!(out.conflicts.len(), 1);
    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&dest.join("tree/same.txt")), "from target");
    assert_eq!(read(&dest.join("tree/new.txt")), "new");
    assert_eq!(read(&dest.join("tree/other.txt")), "other");
    // The skipped file stays in the source, which therefore stays too.
    assert_eq!(read(&tree.join("same.txt")), "from source");
    assert!(!tree.join("new.txt").exists());
}

#[test]
fn move_overwrite_replaces_the_target() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "source");
    let dest = tmp.path().join("out");
    write(&dest.join("a.txt"), "target");

    let out = go_with(
        &request(OpKind::Move, &[&src], Some(&dest)),
        ConflictChoice::Overwrite,
    );

    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&dest.join("a.txt")), "source");
    assert!(!src.exists());
}

#[test]
fn copy_a_folder_into_itself_is_refused() {
    let tmp = tempdir().unwrap();
    let tree = tmp.path().join("tree");
    write(&tree.join("a.txt"), "a");
    fs::create_dir_all(tree.join("sub")).unwrap();

    for dest in [tree.clone(), tree.join("sub")] {
        let out = go(&request(OpKind::Copy, &[&tree], Some(&dest)));
        assert_eq!(out.finished(), (0, 1, false));
        assert!(
            out.errors()[0].contains("into itself"),
            "{:?}",
            out.errors()
        );
    }
    assert!(!tree.join("tree").exists());
    assert!(!tree.join("sub/tree").exists());
}

#[test]
fn move_a_folder_into_its_subtree_is_refused() {
    let tmp = tempdir().unwrap();
    let tree = tmp.path().join("tree");
    write(&tree.join("sub/a.txt"), "a");

    let out = go(&request(OpKind::Move, &[&tree], Some(&tree.join("sub"))));

    assert_eq!(out.finished(), (0, 1, false));
    assert_eq!(read(&tree.join("sub/a.txt")), "a");
}

#[test]
fn copy_onto_the_same_path_is_an_error() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "keep me");

    let out = go(&request(OpKind::Copy, &[&src], Some(tmp.path())));

    assert_eq!(out.finished(), (0, 1, false));
    assert!(out.errors()[0].contains("same"));
    assert_eq!(read(&src), "keep me");
}

#[test]
fn cancel_removes_the_partial_file() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("big.bin");
    fs::write(&src, vec![7u8; 3 * BUFFER_SIZE]).unwrap();
    let small = tmp.path().join("z.txt");
    write(&small, "never copied");
    let dest = tmp.path().join("out");
    let cancel = AtomicBool::new(false);
    let mut out = Vec::new();

    // Raise the flag once the big file is announced: the copy stops before its first
    // chunk is finished and must not leave the file it had already created.
    run(
        &request(OpKind::Copy, &[&src, &small], Some(&dest)),
        &cancel,
        &mut |event| {
            if let OpEvent::Progress(p) = &event
                && p.current == src
            {
                cancel.store(true, Ordering::SeqCst);
            }
            out.push(event);
        },
    );

    assert!(matches!(
        out.last(),
        Some(OpEvent::Finished {
            cancelled: true,
            done_files: 0,
            ..
        })
    ));
    assert!(!dest.join("big.bin").exists());
    assert!(!dest.join("z.txt").exists());
}

#[test]
fn a_raised_flag_before_the_start_does_nothing() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "x");
    let dest = tmp.path().join("out");

    let out = drive(
        &request(OpKind::Copy, &[&src], Some(&dest)),
        &AtomicBool::new(true),
        |_, _| None,
    );

    assert_eq!(out.finished(), (0, 0, true));
    assert!(!dest.join("a.txt").exists());
}

#[test]
fn delete_permanently_removes_files_and_trees() {
    let tmp = tempdir().unwrap();
    let file = tmp.path().join("a.txt");
    write(&file, "x");
    let tree = tmp.path().join("tree");
    write(&tree.join("sub/b.txt"), "bb");
    write(&tree.join("c.txt"), "c");

    let out = go(&request(
        OpKind::Delete { to_trash: false },
        &[&file, &tree],
        None,
    ));

    assert_eq!(out.finished(), (3, 0, false));
    assert!(!file.exists() && !tree.exists());
    let last = out.last_progress();
    assert_eq!((last.total_files, last.done_files), (3, 3));
}

#[test]
fn delete_of_a_missing_path_is_an_error() {
    let tmp = tempdir().unwrap();
    let out = go(&request(
        OpKind::Delete { to_trash: false },
        &[&tmp.path().join("ghost")],
        None,
    ));
    assert_eq!(out.finished(), (0, 1, false));
}

#[cfg(unix)]
#[test]
fn delete_never_follows_a_directory_symlink() {
    let tmp = tempdir().unwrap();
    let real = tmp.path().join("real");
    write(&real.join("keep.txt"), "keep");
    let tree = tmp.path().join("tree");
    fs::create_dir_all(&tree).unwrap();
    std::os::unix::fs::symlink(&real, tree.join("link")).unwrap();
    let top_link = tmp.path().join("toplink");
    std::os::unix::fs::symlink(&real, &top_link).unwrap();

    let out = go(&request(
        OpKind::Delete { to_trash: false },
        &[&tree, &top_link],
        None,
    ));

    assert_eq!(out.finished().1, 0);
    assert!(!tree.exists() && fs::symlink_metadata(&top_link).is_err());
    assert_eq!(read(&real.join("keep.txt")), "keep");
}

#[cfg(unix)]
#[test]
fn copy_keeps_symlinks_as_links() {
    let tmp = tempdir().unwrap();
    let tree = tmp.path().join("tree");
    write(&tree.join("real.txt"), "r");
    std::os::unix::fs::symlink("real.txt", tree.join("link")).unwrap();
    std::os::unix::fs::symlink("missing", tree.join("dangling")).unwrap();
    let dest = tmp.path().join("out");

    let out = go(&request(OpKind::Copy, &[&tree], Some(&dest)));

    assert_eq!(out.finished(), (3, 0, false));
    let link = dest.join("tree/link");
    assert!(
        fs::symlink_metadata(&link)
            .unwrap()
            .file_type()
            .is_symlink()
    );
    assert_eq!(fs::read_link(&link).unwrap(), Path::new("real.txt"));
    assert!(fs::symlink_metadata(dest.join("tree/dangling")).is_ok());
}

#[test]
fn unique_name_numbers_from_two() {
    let tmp = tempdir().unwrap();
    let dir = tmp.path();
    assert_eq!(
        unique_name(dir, "a.txt"),
        dir.join("a.txt"),
        "a free name is kept"
    );
    write(&dir.join("a.txt"), "");
    assert_eq!(unique_name(dir, "a.txt"), dir.join("a (2).txt"));
    write(&dir.join("a (2).txt"), "");
    assert_eq!(unique_name(dir, "a.txt"), dir.join("a (3).txt"));
}

#[test]
fn unique_name_handles_no_extension_and_dot_files() {
    let tmp = tempdir().unwrap();
    let dir = tmp.path();
    write(&dir.join("README"), "");
    write(&dir.join(".bashrc"), "");
    write(&dir.join("a.tar.gz"), "");
    assert_eq!(unique_name(dir, "README"), dir.join("README (2)"));
    assert_eq!(unique_name(dir, ".bashrc"), dir.join(".bashrc (2)"));
    assert_eq!(unique_name(dir, "a.tar.gz"), dir.join("a.tar (2).gz"));
}

#[test]
fn spawn_streams_events_until_finished() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("a.txt");
    write(&src, "x");
    let dest = tmp.path().join("out");

    let rx = spawn(
        request(OpKind::Copy, &[&src], Some(&dest)),
        Arc::new(AtomicBool::new(false)),
    );
    let events: Vec<OpEvent> = rx.iter().collect();

    assert!(matches!(
        events.last(),
        Some(OpEvent::Finished {
            done_files: 1,
            errors: 0,
            cancelled: false
        })
    ));
    assert_eq!(read(&dest.join("a.txt")), "x");
}

#[test]
fn spawn_conflict_is_answered_over_the_channel() {
    let tmp = tempdir().unwrap();
    let (src, dest) = conflict_setup(tmp.path(), OLD, NEW);

    let rx = spawn(
        request(OpKind::Copy, &[&src], Some(&dest)),
        Arc::new(AtomicBool::new(false)),
    );
    let mut finished = false;
    for event in rx {
        match event {
            OpEvent::Conflict { reply, .. } => reply.send(ConflictChoice::Overwrite).unwrap(),
            OpEvent::Finished { done_files, .. } => {
                finished = true;
                assert_eq!(done_files, 1);
            }
            _ => {}
        }
    }

    assert!(finished);
    assert_eq!(read(&dest.join("a.txt")), "source");
}

#[test]
fn log_file_gets_one_line_per_finished_file() {
    let tmp = tempdir().unwrap();
    let src = tmp.path().join("logged.txt");
    write(&src, "x");
    let dest = tmp.path().join("out");
    let log = tmp.path().join("ops.log");

    set_log_file(Some(log.clone()));
    go(&request(OpKind::Copy, &[&src], Some(&dest)));
    go(&request(
        OpKind::Copy,
        &[&tmp.path().join("ghost.txt")],
        Some(&dest),
    ));
    set_log_file(None);

    // Other tests may log here while it is set: look only at our own lines.
    let text = read(&log);
    let ok = text
        .lines()
        .find(|l| l.contains("logged.txt"))
        .expect("a line for the copied file");
    let fields: Vec<&str> = ok.split('\t').collect();
    assert_eq!(fields.len(), 5);
    assert_eq!(fields[1], "COPY");
    assert_eq!(fields[2], src.to_string_lossy());
    assert!(fields[3].ends_with("logged.txt"));
    assert_eq!(fields[4], "OK");
    assert_eq!(fields[0].len(), "2026-10-31 10:00:00".len());
    let err = text
        .lines()
        .find(|l| l.contains("ghost.txt"))
        .expect("a line for the failed file");
    assert!(err.split('\t').nth(4).unwrap().starts_with("ERROR "));
}

/// Moving across devices falls back to copy + delete. Needs a second file system (tmpfs in
/// `/dev/shm`); the test returns early where there is none.
#[cfg(unix)]
#[test]
fn move_across_devices_copies_then_deletes() {
    use std::os::unix::fs::MetadataExt;
    let shm = Path::new("/dev/shm");
    let (Ok(local), Ok(other)) = (tempdir(), tempfile::tempdir_in(shm)) else {
        return;
    };
    let same_device =
        fs::metadata(local.path()).unwrap().dev() == fs::metadata(other.path()).unwrap().dev();
    if same_device {
        return;
    }
    let tree = local.path().join("tree");
    write(&tree.join("sub/a.txt"), "across");
    set_mtime(&tree.join("sub/a.txt"), OLD);

    let out = go(&request(OpKind::Move, &[&tree], Some(other.path())));

    assert_eq!(out.finished(), (1, 0, false));
    assert_eq!(read(&other.path().join("tree/sub/a.txt")), "across");
    assert_eq!(
        mtime(&other.path().join("tree/sub/a.txt")),
        FileTime::from_unix_time(OLD, 0)
    );
    assert!(!tree.exists(), "the source goes once its copy succeeded");
}
