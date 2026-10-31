//! Directory listing, sorting, drives and formatting helpers used by the panels.

use std::cmp::Ordering;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering as AtomicOrdering};
use std::time::SystemTime;

use serde::{Deserialize, Serialize};

/// One row of a panel.
#[derive(Clone, Debug, PartialEq)]
pub struct Entry {
    /// Display name. In branch view this is the path relative to the listed folder.
    pub name: String,
    pub path: PathBuf,
    pub is_dir: bool,
    pub is_link: bool,
    /// The ".." row.
    pub is_parent: bool,
    pub size: u64,
    pub modified: Option<SystemTime>,
    pub hidden: bool,
    pub readonly: bool,
    pub attrs: String,
}

impl Entry {
    /// Extension of a file ("" for folders, dot-files and names without a dot).
    pub fn extension(&self) -> &str {
        if self.is_dir {
            return "";
        }
        split_ext(&self.name).1
    }

    /// Name without its extension (folders keep their full name).
    pub fn stem(&self) -> &str {
        if self.is_dir {
            return &self.name;
        }
        split_ext(&self.name).0
    }

    fn parent_row(dir: &Path) -> Entry {
        Entry {
            name: "..".into(),
            path: dir.parent().map(Path::to_path_buf).unwrap_or_default(),
            is_dir: true,
            is_link: false,
            is_parent: true,
            size: 0,
            modified: None,
            hidden: false,
            readonly: false,
            attrs: String::new(),
        }
    }
}

/// Splits "archive.tar.gz" into ("archive.tar", "gz"); ".bashrc" has no extension.
pub fn split_ext(name: &str) -> (&str, &str) {
    match name.rfind('.') {
        Some(0) | None => (name, ""),
        Some(i) => (&name[..i], &name[i + 1..]),
    }
}

/// Builds the entry for one path (follows nothing: a link is reported as a link).
pub fn entry_from_path(path: &Path) -> io::Result<Entry> {
    let link_meta = fs::symlink_metadata(path)?;
    let is_link = link_meta.file_type().is_symlink();
    // A link to a folder behaves like a folder in the panel.
    let meta = if is_link {
        fs::metadata(path).unwrap_or(link_meta)
    } else {
        link_meta
    };
    let name = path
        .file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_else(|| path.to_string_lossy().into_owned());
    Ok(Entry {
        hidden: is_hidden(&name, &meta),
        readonly: meta.permissions().readonly(),
        attrs: attrs_string(&meta),
        is_dir: meta.is_dir(),
        size: if meta.is_dir() { 0 } else { meta.len() },
        modified: meta.modified().ok(),
        is_link,
        is_parent: false,
        path: path.to_path_buf(),
        name,
    })
}

/// Lists a folder: a ".." row first when the folder has a parent, then every entry
/// (hidden ones only when `show_hidden`). Unreadable entries are skipped.
pub fn list_dir(dir: &Path, show_hidden: bool) -> io::Result<Vec<Entry>> {
    let mut out = Vec::new();
    if dir.parent().is_some() {
        out.push(Entry::parent_row(dir));
    }
    for item in fs::read_dir(dir)? {
        let Ok(item) = item else { continue };
        let Ok(entry) = entry_from_path(&item.path()) else {
            continue;
        };
        if show_hidden || !entry.hidden {
            out.push(entry);
        }
    }
    Ok(out)
}

/// Branch view (Ctrl+B): every file below `dir`, named by its relative path.
/// Stops after `limit` files or when `cancel` is set.
pub fn list_branch(
    dir: &Path,
    show_hidden: bool,
    limit: usize,
    cancel: &AtomicBool,
) -> io::Result<Vec<Entry>> {
    let mut out = Vec::new();
    let walker = walkdir::WalkDir::new(dir).min_depth(1).into_iter();
    let walker = walker.filter_entry(|e| {
        show_hidden || {
            let name = e.file_name().to_string_lossy();
            e.metadata().map(|m| !is_hidden(&name, &m)).unwrap_or(true)
        }
    });
    for item in walker {
        if cancel.load(AtomicOrdering::Relaxed) || out.len() >= limit {
            break;
        }
        let Ok(item) = item else { continue };
        if item.file_type().is_dir() {
            continue;
        }
        let Ok(mut entry) = entry_from_path(item.path()) else {
            continue;
        };
        if let Ok(rel) = item.path().strip_prefix(dir) {
            entry.name = rel.to_string_lossy().into_owned();
        }
        out.push(entry);
    }
    Ok(out)
}

/// Dot-files everywhere; on Windows also the hidden attribute.
pub fn is_hidden(name: &str, meta: &fs::Metadata) -> bool {
    if name.starts_with('.') && name != ".." {
        return true;
    }
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        const FILE_ATTRIBUTE_HIDDEN: u32 = 0x2;
        if meta.file_attributes() & FILE_ATTRIBUTE_HIDDEN != 0 {
            return true;
        }
    }
    let _ = meta;
    false
}

/// "rwxr-xr-x" on Unix, "rahs" letters (or '-') on Windows.
pub fn attrs_string(meta: &fs::Metadata) -> String {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let mode = meta.permissions().mode();
        let mut s = String::with_capacity(9);
        for shift in [6u32, 3, 0] {
            let bits = (mode >> shift) & 0o7;
            s.push(if bits & 4 != 0 { 'r' } else { '-' });
            s.push(if bits & 2 != 0 { 'w' } else { '-' });
            s.push(if bits & 1 != 0 { 'x' } else { '-' });
        }
        s
    }
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        let a = meta.file_attributes();
        let flag = |bit: u32, c: char| if a & bit != 0 { c } else { '-' };
        [
            flag(0x1, 'r'),
            flag(0x20, 'a'),
            flag(0x2, 'h'),
            flag(0x4, 's'),
        ]
        .iter()
        .collect()
    }
    #[cfg(not(any(unix, windows)))]
    {
        if meta.permissions().readonly() {
            "r".into()
        } else {
            "-".into()
        }
    }
}

/// Column a panel is sorted by.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, Serialize, Deserialize)]
pub enum SortKey {
    #[default]
    Name,
    Ext,
    Size,
    Modified,
    Attrs,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, Serialize, Deserialize)]
pub struct SortOrder {
    pub key: SortKey,
    pub descending: bool,
}

/// ".." first, then folders, then files; folders sort by name unless the key is the date.
pub fn sort_entries(entries: &mut [Entry], order: SortOrder) {
    entries.sort_by(|a, b| {
        if a.is_parent != b.is_parent {
            return b.is_parent.cmp(&a.is_parent);
        }
        if a.is_dir != b.is_dir {
            return b.is_dir.cmp(&a.is_dir);
        }
        let key = if a.is_dir && !matches!(order.key, SortKey::Modified) {
            SortKey::Name
        } else {
            order.key
        };
        let ord = match key {
            SortKey::Name => natural_cmp(&a.name, &b.name),
            SortKey::Ext => natural_cmp(a.extension(), b.extension())
                .then_with(|| natural_cmp(&a.name, &b.name)),
            SortKey::Size => a
                .size
                .cmp(&b.size)
                .then_with(|| natural_cmp(&a.name, &b.name)),
            SortKey::Modified => a.modified.cmp(&b.modified),
            SortKey::Attrs => a
                .attrs
                .cmp(&b.attrs)
                .then_with(|| natural_cmp(&a.name, &b.name)),
        };
        if order.descending { ord.reverse() } else { ord }
    });
}

/// Case-insensitive comparison where digit runs compare as numbers ("file2" < "file10").
pub fn natural_cmp(a: &str, b: &str) -> Ordering {
    let mut ai = a.chars().peekable();
    let mut bi = b.chars().peekable();
    loop {
        match (ai.peek().copied(), bi.peek().copied()) {
            (None, None) => return a.cmp(b),
            (None, Some(_)) => return Ordering::Less,
            (Some(_), None) => return Ordering::Greater,
            (Some(ca), Some(cb)) if ca.is_ascii_digit() && cb.is_ascii_digit() => {
                let na = take_number(&mut ai);
                let nb = take_number(&mut bi);
                let ord = na
                    .trim_start_matches('0')
                    .len()
                    .cmp(&nb.trim_start_matches('0').len())
                    .then_with(|| na.trim_start_matches('0').cmp(nb.trim_start_matches('0')));
                if ord != Ordering::Equal {
                    return ord;
                }
            }
            (Some(ca), Some(cb)) => {
                let ord = ca.to_lowercase().cmp(cb.to_lowercase());
                if ord != Ordering::Equal {
                    return ord;
                }
                ai.next();
                bi.next();
            }
        }
    }
}

fn take_number(it: &mut std::iter::Peekable<std::str::Chars<'_>>) -> String {
    let mut s = String::new();
    while let Some(c) = it.peek().copied() {
        if !c.is_ascii_digit() {
            break;
        }
        s.push(c);
        it.next();
    }
    s
}

/// A root the drive buttons offer.
#[derive(Clone, Debug, PartialEq)]
pub struct Drive {
    pub label: String,
    pub path: PathBuf,
    pub total: u64,
    pub free: u64,
}

/// Mounted volumes (drive letters on Windows), the home folder first.
pub fn drives() -> Vec<Drive> {
    let mut out = vec![Drive {
        label: "~".into(),
        path: home_dir(),
        total: 0,
        free: 0,
    }];
    let disks = sysinfo::Disks::new_with_refreshed_list().ok();
    for disk in disks.iter().flat_map(|d| d.iter()) {
        let path = disk.mount_point().to_path_buf();
        let shown = path.to_string_lossy();
        if is_system_mount(&shown) || out.iter().any(|d| d.path == path) {
            continue;
        }
        let label = if cfg!(windows) {
            shown.trim_end_matches('\\').to_string()
        } else {
            match path.file_name() {
                Some(n) => n.to_string_lossy().into_owned(),
                None => "/".into(),
            }
        };
        out.push(Drive {
            label,
            path,
            total: disk.total_space(),
            free: disk.available_space(),
        });
    }
    if !cfg!(windows) && !out.iter().any(|d| d.path == Path::new("/")) {
        out.insert(
            1,
            Drive {
                label: "/".into(),
                path: PathBuf::from("/"),
                total: 0,
                free: 0,
            },
        );
    }
    out
}

fn is_system_mount(p: &str) -> bool {
    const SKIP: [&str; 8] = [
        "/boot",
        "/snap",
        "/proc",
        "/sys",
        "/dev",
        "/run/user",
        "/var/lib",
        "/System/Volumes",
    ];
    SKIP.iter().any(|s| p.starts_with(s)) && p != "/"
}

/// (free, total) bytes of the volume holding `path`.
pub fn free_space(path: &Path) -> Option<(u64, u64)> {
    let disks = sysinfo::Disks::new_with_refreshed_list().ok()?;
    disks
        .iter()
        .filter(|d| path.starts_with(d.mount_point()))
        .max_by_key(|d| d.mount_point().as_os_str().len())
        .map(|d| (d.available_space(), d.total_space()))
}

/// Totals of a folder tree: (bytes, files, folders). Stops early on `cancel`.
pub fn dir_size(path: &Path, cancel: &AtomicBool) -> (u64, u64, u64) {
    let (mut bytes, mut files, mut dirs) = (0u64, 0u64, 0u64);
    for item in walkdir::WalkDir::new(path)
        .min_depth(1)
        .into_iter()
        .flatten()
    {
        if cancel.load(AtomicOrdering::Relaxed) {
            break;
        }
        if item.file_type().is_dir() {
            dirs += 1;
        } else {
            files += 1;
            bytes += item.metadata().map(|m| m.len()).unwrap_or(0);
        }
    }
    (bytes, files, dirs)
}

pub fn home_dir() -> PathBuf {
    directories::BaseDirs::new()
        .map(|d| d.home_dir().to_path_buf())
        .unwrap_or_else(|| PathBuf::from("/"))
}

/// Creates an empty file; fails when it already exists.
pub fn create_file(path: &Path) -> io::Result<()> {
    fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)
        .map(|_| ())
}

/// Checks a file name typed by the user (no separators, not empty, not "." / "..").
pub fn validate_name(name: &str) -> Result<(), String> {
    let trimmed = name.trim();
    if trimmed.is_empty() || trimmed == "." || trimmed == ".." {
        return Err("invalid name".into());
    }
    if name.contains('/')
        || (cfg!(windows) && name.contains(['\\', ':', '*', '?', '"', '<', '>', '|']))
    {
        return Err("the name contains a forbidden character".into());
    }
    Ok(())
}

/// Renames inside the same folder.
pub fn rename_in_place(path: &Path, new_name: &str) -> io::Result<PathBuf> {
    validate_name(new_name).map_err(|e| io::Error::new(io::ErrorKind::InvalidInput, e))?;
    let target = path.with_file_name(new_name);
    if target.exists() && !same_file_ignoring_case(path, &target) {
        return Err(io::Error::new(
            io::ErrorKind::AlreadyExists,
            "a file with that name already exists",
        ));
    }
    fs::rename(path, &target)?;
    Ok(target)
}

fn same_file_ignoring_case(a: &Path, b: &Path) -> bool {
    a.to_string_lossy().to_lowercase() == b.to_string_lossy().to_lowercase()
}

/// "1.5 KB", "12.0 MB", "3 B".
pub fn human_size(n: u64) -> String {
    const UNITS: [&str; 6] = ["B", "KB", "MB", "GB", "TB", "PB"];
    if n < 1024 {
        return format!("{n} B");
    }
    let mut v = n as f64;
    let mut unit = 0;
    while v >= 1024.0 && unit < UNITS.len() - 1 {
        v /= 1024.0;
        unit += 1;
    }
    format!("{v:.1} {}", UNITS[unit])
}

/// "1 234 567" (thin grouping, used in the size column).
pub fn grouped(n: u64) -> String {
    let digits = n.to_string();
    let mut out = String::with_capacity(digits.len() + digits.len() / 3);
    for (i, c) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i).is_multiple_of(3) {
            out.push(' ');
        }
        out.push(c);
    }
    out
}

/// Local "YYYY-MM-DD HH:MM", or "" when unknown.
pub fn format_time(t: Option<SystemTime>) -> String {
    match t {
        Some(t) => {
            let dt: chrono::DateTime<chrono::Local> = t.into();
            dt.format("%Y-%m-%d %H:%M").to_string()
        }
        None => String::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn names(entries: &[Entry]) -> Vec<&str> {
        entries.iter().map(|e| e.name.as_str()).collect()
    }

    #[test]
    fn natural_order_counts_numbers() {
        let mut v = vec!["file10", "File2", "file1", "a"];
        v.sort_by(|a, b| natural_cmp(a, b));
        assert_eq!(v, ["a", "file1", "File2", "file10"]);
    }

    #[test]
    fn split_ext_handles_dot_files() {
        assert_eq!(split_ext(".bashrc"), (".bashrc", ""));
        assert_eq!(split_ext("a.tar.gz"), ("a.tar", "gz"));
        assert_eq!(split_ext("README"), ("README", ""));
    }

    #[test]
    fn list_sort_and_hidden() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir(dir.path().join("zdir")).unwrap();
        fs::write(dir.path().join("b.txt"), "12345").unwrap();
        fs::write(dir.path().join("a.rs"), "1").unwrap();
        fs::write(dir.path().join(".hidden"), "").unwrap();
        fs::write(dir.path().join("été.md"), "").unwrap();

        let mut v = list_dir(dir.path(), false).unwrap();
        sort_entries(&mut v, SortOrder::default());
        assert_eq!(names(&v), ["..", "zdir", "a.rs", "b.txt", "été.md"]);

        let mut v = list_dir(dir.path(), true).unwrap();
        sort_entries(
            &mut v,
            SortOrder {
                key: SortKey::Size,
                descending: true,
            },
        );
        assert_eq!(v[0].name, "..");
        assert_eq!(v[1].name, "zdir");
        assert_eq!(v[2].name, "b.txt");
        assert!(names(&v).contains(&".hidden"));
    }

    #[test]
    fn branch_view_lists_nested_files() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir_all(dir.path().join("a/b")).unwrap();
        fs::write(dir.path().join("a/b/deep.txt"), "x").unwrap();
        fs::write(dir.path().join("top.txt"), "x").unwrap();
        let cancel = AtomicBool::new(false);
        let mut v = list_branch(dir.path(), false, 100, &cancel).unwrap();
        sort_entries(&mut v, SortOrder::default());
        let expected = Path::new("a").join("b").join("deep.txt");
        assert_eq!(names(&v), [expected.to_string_lossy().as_ref(), "top.txt"]);
    }

    #[test]
    fn dir_size_and_rename() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir(dir.path().join("sub")).unwrap();
        fs::write(dir.path().join("sub/x"), "abc").unwrap();
        fs::write(dir.path().join("y"), "de").unwrap();
        let cancel = AtomicBool::new(false);
        assert_eq!(dir_size(dir.path(), &cancel), (5, 2, 1));

        let renamed = rename_in_place(&dir.path().join("y"), "z").unwrap();
        assert!(renamed.exists());
        assert!(rename_in_place(&renamed, "sub").is_err());
        assert!(rename_in_place(&renamed, "a/b").is_err());
    }

    #[test]
    fn create_file_refuses_existing() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("new.txt");
        create_file(&p).unwrap();
        assert!(create_file(&p).is_err());
    }

    #[test]
    fn formatting() {
        assert_eq!(human_size(10), "10 B");
        assert_eq!(human_size(1536), "1.5 KB");
        assert_eq!(grouped(1234567), "1 234 567");
        assert_eq!(grouped(12), "12");
        assert_eq!(format_time(None), "");
    }
}
