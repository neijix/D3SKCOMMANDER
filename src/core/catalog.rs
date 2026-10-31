//! Disk catalog (JBCat / DiskDir style): a text listing of a folder tree that can be
//! browsed later without the disk. First line = root path, then one tab-separated line
//! per entry: `<path>\t<size>\t<YYYY.MM.DD>\t<HH:MM.SS>`.

use std::fs;
use std::io::{self, BufWriter, Write};
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::SystemTime;

use chrono::{DateTime, Local};
use walkdir::WalkDir;

/// Catalog separator inside a path; directories end with it.
const SEP: char = '\\';

fn format_time(t: SystemTime) -> String {
    let dt: DateTime<Local> = t.into();
    dt.format("%Y.%m.%d\t%H:%M.%S").to_string()
}

/// Walk `root` and write its catalog to `out`. Unreadable entries are skipped; the catalog
/// file itself is left out when it lives under `root`. Returns the number of entries.
/// Cancel → `Interrupted` (a partial file is removed).
pub fn write_catalog(root: &Path, out: &Path, cancel: &AtomicBool) -> io::Result<usize> {
    let out_abs = fs::canonicalize(
        out.parent()
            .filter(|p| !p.as_os_str().is_empty())
            .unwrap_or(Path::new(".")),
    )
    .ok()
    .and_then(|d| out.file_name().map(|n| d.join(n)));
    let mut w = BufWriter::new(fs::File::create(out)?);
    let result = write_entries(root, out_abs.as_deref(), &mut w, cancel).and_then(|n| {
        w.flush()?;
        Ok(n)
    });
    drop(w);
    if result.is_err() {
        let _ = fs::remove_file(out);
    }
    result
}

fn write_entries(
    root: &Path,
    skip: Option<&Path>,
    w: &mut impl Write,
    cancel: &AtomicBool,
) -> io::Result<usize> {
    writeln!(w, "{}", root.display())?;
    let mut count = 0;
    let walker = WalkDir::new(root).min_depth(1).sort_by_file_name();
    for entry in walker.into_iter().filter_map(Result::ok) {
        if cancel.load(Ordering::Relaxed) {
            return Err(io::Error::new(io::ErrorKind::Interrupted, "cancelled"));
        }
        if skip.is_some_and(|s| fs::canonicalize(entry.path()).is_ok_and(|p| p == s)) {
            continue;
        }
        let Ok(meta) = entry.metadata() else {
            continue;
        };
        let rel = entry.path().strip_prefix(root).unwrap_or(entry.path());
        let mut path = rel
            .components()
            .map(|c| c.as_os_str().to_string_lossy().into_owned())
            .collect::<Vec<_>>()
            .join(&SEP.to_string());
        let is_dir = entry.file_type().is_dir();
        if is_dir {
            path.push(SEP);
        }
        let size = if is_dir { 0 } else { meta.len() };
        let when = meta.modified().map(format_time).unwrap_or_default();
        writeln!(w, "{path}\t{size}\t{when}")?;
        count += 1;
    }
    Ok(count)
}

#[derive(Clone, PartialEq, Eq, Debug)]
pub struct CatalogEntry {
    /// Path relative to the catalog root, `\`-separated, without the trailing `\` of folders.
    pub path: String,
    pub size: u64,
    pub is_dir: bool,
    /// `YYYY.MM.DD HH:MM.SS` as written in the catalog, when present.
    pub modified: Option<String>,
}

fn parse_entry(line: &str) -> CatalogEntry {
    let mut f = line.split('\t');
    let raw = f.next().unwrap_or_default();
    let size = f.next().and_then(|s| s.trim().parse().ok()).unwrap_or(0);
    let date = f.next().map(str::trim).filter(|s| !s.is_empty());
    let time = f.next().map(str::trim).filter(|s| !s.is_empty());
    let modified = match (date, time) {
        (Some(d), Some(t)) => Some(format!("{d} {t}")),
        (Some(d), None) => Some(d.to_string()),
        _ => None,
    };
    let is_dir = raw.ends_with(SEP);
    CatalogEntry {
        path: raw.trim_end_matches(SEP).to_string(),
        size,
        is_dir,
        modified,
    }
}

/// Read a catalog: its root path and entries. An empty file is `InvalidData`.
pub fn read_catalog(path: &Path) -> io::Result<(String, Vec<CatalogEntry>)> {
    let bytes = fs::read(path)?;
    let text = String::from_utf8_lossy(&bytes);
    let mut lines = text.lines();
    let root = lines
        .next()
        .ok_or_else(|| io::Error::new(io::ErrorKind::InvalidData, "empty catalog file"))?
        .to_string();
    let entries = lines
        .filter(|l| !l.trim().is_empty())
        .map(parse_entry)
        .collect();
    Ok((root, entries))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn no_cancel() -> AtomicBool {
        AtomicBool::new(false)
    }

    fn tree(dir: &Path) {
        fs::create_dir_all(dir.join("docs/deep")).unwrap();
        fs::create_dir(dir.join("empty")).unwrap();
        fs::write(dir.join("a.txt"), b"hello").unwrap();
        fs::write(dir.join("docs/b.md"), b"12").unwrap();
    }

    #[test]
    fn write_then_read_round_trip() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path().join("root");
        fs::create_dir(&root).unwrap();
        tree(&root);
        let out = dir.path().join("cat.txt");
        let n = write_catalog(&root, &out, &no_cancel()).unwrap();
        assert_eq!(n, 5);
        let (r, entries) = read_catalog(&out).unwrap();
        assert_eq!(r, root.display().to_string());
        assert_eq!(entries.len(), 5);
        let find = |p: &str| entries.iter().find(|e| e.path == p).unwrap();
        assert!(find("docs").is_dir);
        assert!(find("docs\\deep").is_dir);
        assert!(find("empty").is_dir);
        assert!(!find("a.txt").is_dir);
        assert_eq!(find("a.txt").size, 5);
        assert_eq!(find("docs\\b.md").size, 2);
        let stamp = find("a.txt").modified.clone().unwrap();
        assert_eq!(stamp.len(), "2026.10.31 10:00.00".len());
        assert!(stamp.contains(' '));
    }

    #[test]
    fn line_format_matches_spec() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path().join("r");
        fs::create_dir(&root).unwrap();
        fs::create_dir(root.join("d")).unwrap();
        fs::write(root.join("d/f"), b"abc").unwrap();
        let out = dir.path().join("c");
        write_catalog(&root, &out, &no_cancel()).unwrap();
        let text = fs::read_to_string(&out).unwrap();
        let lines: Vec<&str> = text.lines().collect();
        assert_eq!(lines[0], root.display().to_string());
        assert!(lines[1].starts_with("d\\\t0\t"));
        assert!(lines[2].starts_with("d\\f\t3\t"));
        let cols: Vec<&str> = lines[2].split('\t').collect();
        assert_eq!(cols.len(), 4);
        assert_eq!(cols[2].matches('.').count(), 2); // YYYY.MM.DD
        assert!(cols[3].contains(':') && cols[3].contains('.')); // HH:MM.SS
    }

    #[test]
    fn catalog_inside_root_is_not_listed() {
        let dir = tempfile::tempdir().unwrap();
        tree(dir.path());
        let out = dir.path().join("self.cat");
        let n = write_catalog(dir.path(), &out, &no_cancel()).unwrap();
        assert_eq!(n, 5);
        let (_, entries) = read_catalog(&out).unwrap();
        assert!(entries.iter().all(|e| e.path != "self.cat"));
    }

    #[test]
    fn cancel_removes_output() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path().join("r");
        fs::create_dir(&root).unwrap();
        fs::write(root.join("a"), b"x").unwrap();
        let out = dir.path().join("c");
        let err = write_catalog(&root, &out, &AtomicBool::new(true)).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Interrupted);
        assert!(!out.exists());
    }

    #[test]
    fn read_tolerates_short_lines_and_empty_file() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("c");
        fs::write(&p, "/root\nx\\\nfile\t7\n").unwrap();
        let (root, e) = read_catalog(&p).unwrap();
        assert_eq!(root, "/root");
        assert_eq!(
            e[0],
            CatalogEntry {
                path: "x".into(),
                size: 0,
                is_dir: true,
                modified: None
            }
        );
        assert_eq!(e[1].size, 7);
        fs::write(&p, "").unwrap();
        assert_eq!(
            read_catalog(&p).unwrap_err().kind(),
            io::ErrorKind::InvalidData
        );
    }
}
