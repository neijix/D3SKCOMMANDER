//! Archives: list, extract and create ZIP, TAR, TAR.GZ and 7Z natively; RAR and the other
//! formats through an installed `7z` / `unrar`. Extraction refuses every path that would
//! land outside the destination (zip-slip): absolute paths, `..` components, drive and UNC
//! prefixes. Links stored in an archive are never recreated.

mod common;
mod external;
mod sevenz;
mod tar_fmt;
mod zip_fmt;

use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

pub use external::external_tool;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum ArchiveKind {
    Zip,
    Tar,
    TarGz,
    SevenZ,
    /// rar, arj, lzh, cab, … via `7z` / `unrar`.
    External,
}

impl ArchiveKind {
    /// By extension, case-insensitive.
    pub fn from_path(p: &Path) -> Option<Self> {
        let name = p.file_name()?.to_string_lossy().to_lowercase();
        let ends = |suffixes: &[&str]| suffixes.iter().any(|s| name.ends_with(s));
        if ends(&[".tar.gz", ".tgz"]) {
            Some(Self::TarGz)
        } else if ends(&[".zip", ".jar"]) {
            Some(Self::Zip)
        } else if ends(&[".tar"]) {
            Some(Self::Tar)
        } else if ends(&[".7z"]) {
            Some(Self::SevenZ)
        } else if ends(&[
            ".rar", ".arj", ".lzh", ".lha", ".cab", ".ace", ".uc2", ".gz", ".bz2", ".xz", ".iso",
        ]) {
            Some(Self::External)
        } else {
            None
        }
    }

    /// The kinds `create` can write.
    pub fn creatable() -> &'static [ArchiveKind] {
        &[Self::Zip, Self::Tar, Self::TarGz, Self::SevenZ]
    }

    /// File extension without the dot (empty for `External`, which cannot be created).
    pub fn extension(&self) -> &'static str {
        match self {
            Self::Zip => "zip",
            Self::Tar => "tar",
            Self::TarGz => "tar.gz",
            Self::SevenZ => "7z",
            Self::External => "",
        }
    }

    pub fn label(&self) -> &'static str {
        match self {
            Self::Zip => "ZIP",
            Self::Tar => "TAR",
            Self::TarGz => "TAR.GZ",
            Self::SevenZ => "7-Zip",
            Self::External => "RAR / other (external tool)",
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ArchiveEntry {
    /// `/`-separated, no leading `/`.
    pub path: String,
    pub size: u64,
    pub compressed_size: Option<u64>,
    pub is_dir: bool,
    pub modified: Option<SystemTime>,
}

fn kind_of(archive: &Path) -> io::Result<ArchiveKind> {
    ArchiveKind::from_path(archive).ok_or_else(|| {
        io::Error::new(
            io::ErrorKind::Unsupported,
            format!("unknown archive type: {}", archive.display()),
        )
    })
}

/// Every entry of the archive, including the parent folders it only implies.
pub fn list(archive: &Path) -> io::Result<Vec<ArchiveEntry>> {
    match kind_of(archive)? {
        ArchiveKind::Zip => zip_fmt::list(archive),
        ArchiveKind::Tar => tar_fmt::list(archive, false),
        ArchiveKind::TarGz => tar_fmt::list(archive, true),
        ArchiveKind::SevenZ => sevenz::list(archive),
        ArchiveKind::External => external::list(archive),
    }
}

/// Extracts `entries` (`None` = everything; a folder takes its whole subtree) into `dest`.
/// Paths are kept relative to the deepest common parent of the selection, as when
/// extracting from a browsed sub-folder. `progress` gets each entry's path before it is
/// written; returning `false` stops (the files already written stay). Returns the number
/// of files written.
pub fn extract(
    archive: &Path,
    entries: Option<&[String]>,
    dest: &Path,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<usize> {
    let kind = kind_of(archive)?;
    let plan = common::Plan::new(entries);
    if kind != ArchiveKind::External {
        fs::create_dir_all(dest)?;
    }
    match kind {
        ArchiveKind::Zip => zip_fmt::extract(archive, &plan, dest, progress),
        ArchiveKind::Tar => tar_fmt::extract(archive, false, &plan, dest, progress),
        ArchiveKind::TarGz => tar_fmt::extract(archive, true, &plan, dest, progress),
        ArchiveKind::SevenZ => sevenz::extract(archive, &plan, dest, progress),
        ArchiveKind::External => external::extract(archive, &plan, dest, progress),
    }
}

/// Creates `archive` of `kind` from `sources`, each stored under its own file name,
/// recursively. `level` is 0 (store) to 9. `progress` gets each entry's archive path;
/// returning `false` cancels: the partial archive is removed and the error kind is
/// `Interrupted`. Returns the number of files stored.
pub fn create(
    archive: &Path,
    kind: ArchiveKind,
    sources: &[PathBuf],
    level: u8,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<usize> {
    if kind == ArchiveKind::External {
        return Err(io::Error::new(
            io::ErrorKind::Unsupported,
            "this format cannot be created",
        ));
    }
    // written beside the target and renamed at the end: a failure never destroys an old archive
    let mut part = archive.as_os_str().to_os_string();
    part.push(".part");
    let part = PathBuf::from(part);
    let items = common::collect_sources(sources, &[archive, &part])?;
    let result = match kind {
        ArchiveKind::Zip => zip_fmt::create(&part, &items, level, progress),
        ArchiveKind::Tar => tar_fmt::create(&part, false, &items, level, progress),
        ArchiveKind::TarGz => tar_fmt::create(&part, true, &items, level, progress),
        ArchiveKind::SevenZ => sevenz::create(&part, &items, level, progress),
        ArchiveKind::External => Ok(None),
    };
    match result {
        Ok(Some(count)) => {
            fs::rename(&part, archive).inspect_err(|_| {
                let _ = fs::remove_file(&part);
            })?;
            Ok(count)
        }
        Ok(None) => {
            let _ = fs::remove_file(&part);
            Err(common::cancelled())
        }
        Err(e) => {
            let _ = fs::remove_file(&part);
            Err(e)
        }
    }
}

/// Direct children of folder `inner` (`""` = root): browse an archive like a folder.
pub fn list_dir<'a>(entries: &'a [ArchiveEntry], inner: &str) -> Vec<&'a ArchiveEntry> {
    let inner = inner.trim_matches('/');
    entries
        .iter()
        .filter(|e| {
            let rest = if inner.is_empty() {
                Some(e.path.as_str())
            } else {
                e.path.strip_prefix(inner).and_then(|r| r.strip_prefix('/'))
            };
            rest.is_some_and(|r| !r.is_empty() && !r.contains('/'))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use filetime::FileTime;
    use std::fs::File;
    use std::io::Write;

    const UNICODE_NAME: &str = "données-é-日本.txt";

    fn noop() -> impl FnMut(&str) -> bool {
        |_| true
    }

    /// proj/{top.txt, docs/readme.txt, docs/sub/<unicode>.txt, empty/}
    fn make_tree(root: &Path) -> PathBuf {
        let proj = root.join("proj");
        fs::create_dir_all(proj.join("docs/sub")).unwrap();
        fs::create_dir_all(proj.join("empty")).unwrap();
        fs::write(proj.join("top.txt"), "top").unwrap();
        fs::write(proj.join("docs/readme.txt"), "hello readme").unwrap();
        fs::write(proj.join("docs/sub").join(UNICODE_NAME), "unicode body").unwrap();
        let t = FileTime::from_unix_time(1_600_000_000, 0);
        filetime::set_file_mtime(proj.join("docs/readme.txt"), t).unwrap();
        proj
    }

    fn archive_path(root: &Path, kind: ArchiveKind) -> PathBuf {
        root.join(format!("out.{}", kind.extension()))
    }

    fn built(root: &Path, kind: ArchiveKind, level: u8) -> PathBuf {
        let proj = make_tree(root);
        let archive = archive_path(root, kind);
        let n = create(&archive, kind, &[proj], level, &mut noop()).unwrap();
        assert_eq!(n, 3, "{kind:?}");
        archive
    }

    fn find<'a>(entries: &'a [ArchiveEntry], path: &str) -> &'a ArchiveEntry {
        entries
            .iter()
            .find(|e| e.path == path)
            .unwrap_or_else(|| panic!("{path} not listed in {entries:?}"))
    }

    #[test]
    fn from_path_by_extension() {
        let k = |s: &str| ArchiveKind::from_path(Path::new(s));
        assert_eq!(k("a.zip"), Some(ArchiveKind::Zip));
        assert_eq!(k("A.JAR"), Some(ArchiveKind::Zip));
        assert_eq!(k("a.tar"), Some(ArchiveKind::Tar));
        assert_eq!(k("a.tar.gz"), Some(ArchiveKind::TarGz));
        assert_eq!(k("dir/A.TGZ"), Some(ArchiveKind::TarGz));
        assert_eq!(k("a.7z"), Some(ArchiveKind::SevenZ));
        for ext in [
            "rar", "arj", "lzh", "lha", "cab", "ace", "uc2", "gz", "bz2", "xz", "iso",
        ] {
            assert_eq!(k(&format!("a.{ext}")), Some(ArchiveKind::External), "{ext}");
        }
        assert_eq!(k("a.tar.bz2"), Some(ArchiveKind::External));
        assert_eq!(k("a.txt"), None);
        assert_eq!(k("zip"), None);
    }

    #[test]
    fn kind_metadata() {
        assert_eq!(
            ArchiveKind::creatable(),
            &[
                ArchiveKind::Zip,
                ArchiveKind::Tar,
                ArchiveKind::TarGz,
                ArchiveKind::SevenZ
            ]
        );
        let exts: Vec<_> = ArchiveKind::creatable()
            .iter()
            .map(|k| k.extension())
            .collect();
        assert_eq!(exts, ["zip", "tar", "tar.gz", "7z"]);
        assert!(!ArchiveKind::Zip.label().is_empty());
    }

    #[test]
    fn round_trip_every_creatable_kind() {
        for &kind in ArchiveKind::creatable() {
            for level in [0u8, 6, 9] {
                let tmp = tempfile::tempdir().unwrap();
                let archive = built(tmp.path(), kind, level);
                let entries = list(&archive).unwrap();

                for dir in ["proj", "proj/docs", "proj/docs/sub", "proj/empty"] {
                    assert!(find(&entries, dir).is_dir, "{kind:?} {dir}");
                }
                let uni = format!("proj/docs/sub/{UNICODE_NAME}");
                let e = find(&entries, &uni);
                assert!(!e.is_dir);
                assert_eq!(e.size, "unicode body".len() as u64, "{kind:?}");

                let out = tmp.path().join("extracted");
                let n = extract(&archive, None, &out, &mut noop()).unwrap();
                assert_eq!(n, 3, "{kind:?}");
                let proj = out.join("proj");
                assert_eq!(fs::read_to_string(proj.join("top.txt")).unwrap(), "top");
                assert_eq!(
                    fs::read_to_string(proj.join("docs/sub").join(UNICODE_NAME)).unwrap(),
                    "unicode body"
                );
                assert!(proj.join("empty").is_dir(), "{kind:?}");

                let mtime = fs::metadata(proj.join("docs/readme.txt"))
                    .unwrap()
                    .modified()
                    .unwrap();
                let secs = FileTime::from_system_time(mtime).unix_seconds();
                assert!((secs - 1_600_000_000).abs() <= 2, "{kind:?} mtime {secs}");
            }
        }
    }

    #[test]
    fn listed_modified_time_is_kept() {
        for &kind in ArchiveKind::creatable() {
            let tmp = tempfile::tempdir().unwrap();
            let archive = built(tmp.path(), kind, 6);
            let entries = list(&archive).unwrap();
            let m = find(&entries, "proj/docs/readme.txt").modified.unwrap();
            let secs = FileTime::from_system_time(m).unix_seconds();
            assert!((secs - 1_600_000_000).abs() <= 2, "{kind:?} {secs}");
        }
    }

    #[test]
    fn extract_a_selected_subfolder() {
        for &kind in ArchiveKind::creatable() {
            let tmp = tempfile::tempdir().unwrap();
            let archive = built(tmp.path(), kind, 6);
            let out = tmp.path().join("sel");
            let sel = vec!["proj/docs".to_string()];
            let n = extract(&archive, Some(&sel), &out, &mut noop()).unwrap();
            assert_eq!(n, 2, "{kind:?}");
            assert!(out.join("docs/readme.txt").is_file(), "{kind:?}");
            assert!(out.join("docs/sub").join(UNICODE_NAME).is_file());
            assert!(!out.join("top.txt").exists());
            assert!(!out.join("proj").exists());
        }
    }

    #[test]
    fn extract_selection_keeps_paths_below_the_common_parent() {
        let tmp = tempfile::tempdir().unwrap();
        let archive = built(tmp.path(), ArchiveKind::Zip, 6);

        let one = tmp.path().join("one");
        let sel = vec!["proj/docs/readme.txt".to_string()];
        extract(&archive, Some(&sel), &one, &mut noop()).unwrap();
        assert!(one.join("readme.txt").is_file());

        let two = tmp.path().join("two");
        let sel = vec![
            "proj/top.txt".to_string(),
            "proj/docs/readme.txt".to_string(),
        ];
        extract(&archive, Some(&sel), &two, &mut noop()).unwrap();
        assert!(two.join("top.txt").is_file());
        assert!(two.join("docs/readme.txt").is_file());

        let none = tmp.path().join("none");
        assert_eq!(extract(&archive, Some(&[]), &none, &mut noop()).unwrap(), 0);
    }

    #[test]
    fn selection_does_not_match_name_prefixes() {
        let tmp = tempfile::tempdir().unwrap();
        let proj = tmp.path().join("p");
        fs::create_dir_all(proj.join("doc")).unwrap();
        fs::create_dir_all(proj.join("docs")).unwrap();
        fs::write(proj.join("doc/a.txt"), "a").unwrap();
        fs::write(proj.join("docs/b.txt"), "b").unwrap();
        let archive = tmp.path().join("p.tar");
        create(&archive, ArchiveKind::Tar, &[proj], 0, &mut noop()).unwrap();
        let out = tmp.path().join("o");
        let sel = vec!["p/doc".to_string()];
        extract(&archive, Some(&sel), &out, &mut noop()).unwrap();
        assert!(out.join("doc/a.txt").is_file());
        assert!(!out.join("docs").exists());
    }

    #[test]
    fn list_dir_browses_like_a_folder() {
        let tmp = tempfile::tempdir().unwrap();
        let archive = built(tmp.path(), ArchiveKind::Tar, 0);
        let entries = list(&archive).unwrap();
        let names = |inner: &str| -> Vec<String> {
            let mut v: Vec<_> = list_dir(&entries, inner)
                .iter()
                .map(|e| e.path.clone())
                .collect();
            v.sort();
            v
        };
        assert_eq!(names(""), ["proj"]);
        assert_eq!(names("proj"), ["proj/docs", "proj/empty", "proj/top.txt"]);
        assert_eq!(names("proj/"), names("proj"));
        assert_eq!(
            names("proj/docs"),
            ["proj/docs/readme.txt", "proj/docs/sub"]
        );
        assert!(names("proj/empty").is_empty());
        assert!(names("nope").is_empty());
        assert!(names("pro").is_empty());
    }

    #[test]
    fn list_includes_implied_parent_dirs() {
        let tmp = tempfile::tempdir().unwrap();
        let archive = tmp.path().join("x.zip");
        let mut zip = zip::ZipWriter::new(File::create(&archive).unwrap());
        let opts = zip::write::SimpleFileOptions::default();
        zip.start_file("a/b/c.txt", opts).unwrap();
        zip.write_all(b"c").unwrap();
        zip.finish().unwrap();
        let entries = list(&archive).unwrap();
        let paths: Vec<_> = entries
            .iter()
            .map(|e| (e.path.as_str(), e.is_dir))
            .collect();
        assert_eq!(paths, [("a", true), ("a/b", true), ("a/b/c.txt", false)]);
    }

    #[test]
    fn unsafe_names_are_recognised() {
        for bad in [
            "/etc/passwd",
            "\\\\server\\share\\x",
            "C:\\win\\x",
            "c:/x",
            "a/../../b",
            "..",
            "a\\..\\..\\b",
            "a\0b",
        ] {
            assert!(common::unsafe_reason(bad).is_some(), "{bad:?}");
        }
        for good in ["a/b.txt", "./a/b", "a/./b", "dir/", "weird..name", "ab:c/d"] {
            assert!(common::unsafe_reason(good).is_none(), "{good:?}");
        }
    }

    /// `root/out` is the destination, `root/evil.txt` what a slip would create.
    fn assert_slip_rejected(archive: &Path, root: &Path) {
        let dest = root.join("out");
        let err = extract(archive, None, &dest, &mut noop()).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::InvalidData, "{err}");
        assert!(err.to_string().contains("unsafe path"), "{err}");
        assert!(!root.join("evil.txt").exists());
        assert!(!dest.join("evil.txt").exists());
    }

    #[test]
    fn zip_slip_is_rejected_in_zip() {
        for name in [
            "../evil.txt",
            "/abs-evil.txt",
            "ok/../../evil.txt",
            "C:\\evil.txt",
        ] {
            let tmp = tempfile::tempdir().unwrap();
            let archive = tmp.path().join("slip.zip");
            let mut zip = zip::ZipWriter::new(File::create(&archive).unwrap());
            zip.start_file(name, zip::write::SimpleFileOptions::default())
                .unwrap();
            zip.write_all(b"pwned").unwrap();
            zip.finish().unwrap();
            assert_slip_rejected(&archive, tmp.path());
        }
    }

    #[test]
    fn zip_slip_is_rejected_in_tar_and_targz() {
        for gz in [false, true] {
            for name in ["../evil.txt", "/abs-evil.txt", "a/../../evil.txt"] {
                let tmp = tempfile::tempdir().unwrap();
                let archive = tmp.path().join(if gz { "s.tar.gz" } else { "s.tar" });
                let mut header = tar::Header::new_gnu();
                header.as_old_mut().name[..name.len()].copy_from_slice(name.as_bytes());
                header.set_size(5);
                header.set_entry_type(tar::EntryType::Regular);
                header.set_mode(0o644);
                header.set_cksum();
                let file = File::create(&archive).unwrap();
                if gz {
                    let enc = flate2::write::GzEncoder::new(file, flate2::Compression::fast());
                    let mut b = tar::Builder::new(enc);
                    b.append(&header, &b"pwned"[..]).unwrap();
                    b.into_inner().unwrap().finish().unwrap();
                } else {
                    let mut b = tar::Builder::new(file);
                    b.append(&header, &b"pwned"[..]).unwrap();
                    b.into_inner().unwrap();
                }
                assert_slip_rejected(&archive, tmp.path());
            }
        }
    }

    #[test]
    fn zip_slip_is_rejected_in_7z() {
        let tmp = tempfile::tempdir().unwrap();
        let archive = tmp.path().join("slip.7z");
        let mut w = sevenz_rust2::ArchiveWriter::create(&archive).unwrap();
        let entry = sevenz_rust2::ArchiveEntry::new_file("../evil.txt");
        w.push_archive_entry(entry, Some(&b"pwned"[..])).unwrap();
        w.finish().unwrap();
        assert_slip_rejected(&archive, tmp.path());
    }

    #[test]
    fn links_in_archives_are_not_recreated() {
        let tmp = tempfile::tempdir().unwrap();
        let archive = tmp.path().join("link.tar");
        let mut b = tar::Builder::new(File::create(&archive).unwrap());
        let mut h = tar::Header::new_gnu();
        h.set_entry_type(tar::EntryType::Symlink);
        h.set_size(0);
        b.append_link(&mut h, "evil-link", "/etc").unwrap();
        b.into_inner().unwrap();
        let out = tmp.path().join("out");
        assert_eq!(extract(&archive, None, &out, &mut noop()).unwrap(), 0);
        assert!(fs::symlink_metadata(out.join("evil-link")).is_err());
        assert_eq!(list(&archive).unwrap().len(), 1);
    }

    #[test]
    fn create_cancel_removes_the_partial_archive() {
        for &kind in ArchiveKind::creatable() {
            let tmp = tempfile::tempdir().unwrap();
            let proj = make_tree(tmp.path());
            let archive = archive_path(tmp.path(), kind);
            let mut seen = 0;
            let err = create(&archive, kind, &[proj], 6, &mut |_| {
                seen += 1;
                seen < 3
            })
            .unwrap_err();
            assert_eq!(err.kind(), io::ErrorKind::Interrupted, "{kind:?}");
            assert!(!archive.exists(), "{kind:?}");
            assert!(!tmp.path().join("out.part").exists());
            let leftovers: Vec<_> = fs::read_dir(tmp.path()).unwrap().collect();
            assert_eq!(leftovers.len(), 1, "only proj remains: {kind:?}");
        }
    }

    #[test]
    fn extract_cancel_stops_early() {
        let tmp = tempfile::tempdir().unwrap();
        let archive = built(tmp.path(), ArchiveKind::Zip, 6);
        let out = tmp.path().join("o");
        let mut calls = 0;
        let n = extract(&archive, None, &out, &mut |_| {
            calls += 1;
            calls < 3
        })
        .unwrap();
        assert!(n < 3);
        assert_eq!(calls, 3);
    }

    #[test]
    fn failed_create_keeps_an_existing_archive() {
        let tmp = tempfile::tempdir().unwrap();
        let archive = tmp.path().join("keep.zip");
        fs::write(&archive, "old").unwrap();
        let missing = tmp.path().join("does-not-exist");
        let err = create(&archive, ArchiveKind::Zip, &[missing], 6, &mut noop());
        assert!(err.is_err());
        assert_eq!(fs::read_to_string(&archive).unwrap(), "old");
    }

    #[test]
    fn the_archive_is_not_stored_in_itself() {
        let tmp = tempfile::tempdir().unwrap();
        let proj = make_tree(tmp.path());
        let archive = proj.join("self.tar");
        fs::write(&archive, "old").unwrap();
        let n = create(&archive, ArchiveKind::Tar, &[proj], 0, &mut noop()).unwrap();
        assert_eq!(n, 3);
        let entries = list(&archive).unwrap();
        assert!(entries.iter().all(|e| e.path != "proj/self.tar"));
    }

    #[test]
    fn unknown_and_external_kinds() {
        let tmp = tempfile::tempdir().unwrap();
        let txt = tmp.path().join("a.txt");
        fs::write(&txt, "x").unwrap();
        assert_eq!(list(&txt).unwrap_err().kind(), io::ErrorKind::Unsupported);
        let err = create(
            &tmp.path().join("a.rar"),
            ArchiveKind::External,
            &[txt],
            1,
            &mut noop(),
        )
        .unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Unsupported);
    }

    #[test]
    fn external_slt_output_is_parsed() {
        let text = "\n7-Zip 23.01\n\nListing archive: a.rar\n\n--\nPath = a.rar\nType = Rar\n\n----------\n\
Path = dir\nFolder = +\nSize = 0\nPacked Size = 0\nModified = 2024-01-02 03:04:05\nAttributes = D\n\n\
Path = dir/f\u{e9}.txt\nFolder = -\nSize = 12\nPacked Size = 10\nModified = 2024-01-02 03:04:05.1234567\nAttributes = A\n\n\
Path = win\\x.bin\r\nSize = 3\n";
        let e = external::parse_slt(text);
        assert_eq!(e.len(), 3);
        assert!(e[0].is_dir && e[0].path == "dir");
        assert_eq!(e[1].path, "dir/f\u{e9}.txt");
        assert_eq!((e[1].size, e[1].compressed_size), (12, Some(10)));
        assert!(e[1].modified.is_some() && !e[1].is_dir);
        assert_eq!(e[2].path, "win/x.bin");
        assert!(external::parse_slt("no listing here").is_empty());
    }

    #[test]
    fn external_lb_output_is_parsed() {
        let e = external::parse_lb("a\r\na/b.txt\nc.txt\n");
        assert_eq!(e.len(), 3);
        assert!(e[0].is_dir && !e[1].is_dir && !e[2].is_dir);
    }

    #[test]
    fn external_tool_round_trip_when_installed() {
        let Some(tool) = external_tool() else {
            eprintln!("skipped: no 7z/unrar on PATH");
            return;
        };
        let tmp = tempfile::tempdir().unwrap();
        let archive = built(tmp.path(), ArchiveKind::Zip, 6);
        // 7z reads zip too; unrar does not
        if tool
            .file_stem()
            .is_some_and(|s| s.to_string_lossy().starts_with("unrar"))
        {
            eprintln!("skipped: unrar cannot read zip");
            return;
        }
        let entries = external::list_with(&tool, &archive).unwrap();
        assert!(find(&entries, "proj/top.txt").size == 3);
        let out = tmp.path().join("ext");
        let plan = common::Plan::new(Some(&["proj/docs".to_string()]));
        let n = external::extract_with(&tool, &archive, &plan, &out, &mut noop()).unwrap();
        assert_eq!(n, 2);
        assert!(out.join("docs/readme.txt").is_file());
        assert!(!out.join("proj").exists());
    }

    #[test]
    fn missing_external_tool_is_reported_as_unsupported() {
        if external_tool().is_some() {
            eprintln!("skipped: an external tool is installed");
            return;
        }
        let tmp = tempfile::tempdir().unwrap();
        let rar = tmp.path().join("a.rar");
        fs::write(&rar, "Rar!").unwrap();
        let err = list(&rar).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Unsupported);
        assert!(err.to_string().contains("7z"), "{err}");
        let err = extract(&rar, None, &tmp.path().join("o"), &mut noop()).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Unsupported);
    }
}
