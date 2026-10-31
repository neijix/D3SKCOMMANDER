//! Helpers shared by every archive format: selection, zip-slip checks, file writing and
//! source collection for `create`.

use std::fs::{self, File};
use std::io::{self, Read};
use std::path::{Path, PathBuf};
use std::time::SystemTime;

use walkdir::WalkDir;

use super::ArchiveEntry;

pub(super) fn invalid(msg: impl Into<String>) -> io::Error {
    io::Error::new(io::ErrorKind::InvalidData, msg.into())
}

pub(super) fn cancelled() -> io::Error {
    io::Error::new(io::ErrorKind::Interrupted, "cancelled")
}

/// Splits an archive member name on both separators, dropping empty and `.` parts.
fn parts(raw: &str) -> Vec<&str> {
    raw.split(['/', '\\'])
        .filter(|p| !p.is_empty() && *p != ".")
        .collect()
}

/// Canonical `/`-separated form of a member name: no leading `/`, no `.` parts.
/// `..` parts are kept so unsafe names stay recognisable.
pub(super) fn normalize(raw: &str) -> String {
    parts(raw).join("/")
}

/// Why a member name must never be written below the destination, if it must not.
pub(super) fn unsafe_reason(raw: &str) -> Option<&'static str> {
    if raw.contains('\0') {
        return Some("NUL byte");
    }
    if raw.starts_with(['/', '\\']) {
        return Some("absolute path");
    }
    for part in parts(raw) {
        if part == ".." {
            return Some("parent directory component");
        }
        let b = part.as_bytes();
        if b.len() >= 2 && b[0].is_ascii_alphabetic() && b[1] == b':' {
            return Some("drive prefix");
        }
    }
    None
}

/// What to extract and how to strip the browsed folder from the stored paths.
pub(super) struct Plan {
    selection: Option<Vec<String>>,
    base: String,
}

impl Plan {
    pub(super) fn new(entries: Option<&[String]>) -> Plan {
        let Some(entries) = entries else {
            return Plan {
                selection: None,
                base: String::new(),
            };
        };
        let sel: Vec<String> = entries.iter().map(|e| normalize(e)).collect();
        let base = common_parent(&sel);
        Plan {
            selection: Some(sel),
            base,
        }
    }

    /// The normalized selection (`None` = everything).
    pub(super) fn selection(&self) -> Option<&[String]> {
        self.selection.as_deref()
    }

    pub(super) fn wants(&self, norm: &str) -> bool {
        match &self.selection {
            None => true,
            Some(sel) => sel.iter().any(|s| {
                s.is_empty()
                    || norm == s
                    || norm
                        .strip_prefix(s.as_str())
                        .is_some_and(|rest| rest.starts_with('/'))
            }),
        }
    }

    /// The path of `norm` below the destination (the browsed folder stripped).
    pub(super) fn relative<'a>(&self, norm: &'a str) -> &'a str {
        if self.base.is_empty() {
            return norm;
        }
        norm.strip_prefix(self.base.as_str())
            .and_then(|r| r.strip_prefix('/'))
            .unwrap_or(norm)
    }
}

/// Deepest folder holding every selected entry (the entries' parents' common prefix).
fn common_parent(sel: &[String]) -> String {
    let mut common: Option<Vec<&str>> = None;
    for s in sel {
        let mut comps: Vec<&str> = s.split('/').filter(|c| !c.is_empty()).collect();
        comps.pop();
        common = Some(match common {
            None => comps,
            Some(prev) => prev
                .into_iter()
                .zip(comps)
                .take_while(|(a, b)| a == b)
                .map(|(a, _)| a)
                .collect(),
        });
    }
    common.unwrap_or_default().join("/")
}

/// Resolves a member name to its destination path; `Ok(None)` = not selected / nothing to do.
pub(super) fn resolve(plan: &Plan, dest: &Path, raw: &str) -> io::Result<Option<PathBuf>> {
    let norm = normalize(raw);
    if !plan.wants(&norm) {
        return Ok(None);
    }
    if let Some(reason) = unsafe_reason(raw) {
        return Err(invalid(format!("unsafe path in archive ({reason}): {raw}")));
    }
    let rel = plan.relative(&norm);
    if rel.is_empty() {
        return Ok(None);
    }
    Ok(Some(
        rel.split('/').fold(dest.to_path_buf(), |p, c| p.join(c)),
    ))
}

pub(super) struct EntryMeta {
    pub is_dir: bool,
    pub modified: Option<SystemTime>,
    pub mode: Option<u32>,
}

pub(super) enum Step {
    File,
    Dir,
    Skipped,
    Stop,
}

/// Extracts one member (shared by the Zip, Tar and 7z readers).
pub(super) fn extract_entry(
    plan: &Plan,
    dest: &Path,
    raw: &str,
    meta: &EntryMeta,
    data: &mut dyn Read,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<Step> {
    let Some(out) = resolve(plan, dest, raw)? else {
        return Ok(Step::Skipped);
    };
    if !progress(&normalize(raw)) {
        return Ok(Step::Stop);
    }
    if meta.is_dir {
        fs::create_dir_all(&out)?;
        return Ok(Step::Dir);
    }
    write_file(&out, data, meta)?;
    Ok(Step::File)
}

fn write_file(out: &Path, data: &mut dyn Read, meta: &EntryMeta) -> io::Result<()> {
    if let Some(parent) = out.parent() {
        fs::create_dir_all(parent)?;
    }
    // never write through a symlink that was already there
    if fs::symlink_metadata(out).is_ok_and(|m| m.file_type().is_symlink()) {
        fs::remove_file(out)?;
    }
    let mut file = File::create(out)?;
    io::copy(data, &mut file)?;
    drop(file);
    if let Some(t) = meta.modified {
        // a failure to set the date must not fail the extraction
        let _ = filetime::set_file_mtime(out, filetime::FileTime::from_system_time(t));
    }
    set_mode(out, meta.mode);
    Ok(())
}

#[cfg(unix)]
fn set_mode(path: &Path, mode: Option<u32>) {
    use std::os::unix::fs::PermissionsExt;
    if let Some(mode) = mode {
        // keep the owner able to read and replace the file; drop setuid/setgid/sticky
        let _ = fs::set_permissions(path, fs::Permissions::from_mode((mode & 0o777) | 0o600));
    }
}

#[cfg(not(unix))]
fn set_mode(_path: &Path, _mode: Option<u32>) {}

#[cfg(unix)]
pub(super) fn mode_of(meta: &fs::Metadata) -> Option<u32> {
    use std::os::unix::fs::PermissionsExt;
    Some(meta.permissions().mode() & 0o7777)
}

#[cfg(not(unix))]
pub(super) fn mode_of(_meta: &fs::Metadata) -> Option<u32> {
    None
}

/// Adds the parent folders no member names explicitly, each before its first child.
pub(super) fn with_implied_dirs(entries: Vec<ArchiveEntry>) -> Vec<ArchiveEntry> {
    use std::collections::HashSet;
    let explicit: HashSet<String> = entries
        .iter()
        .filter(|e| e.is_dir)
        .map(|e| e.path.clone())
        .collect();
    let mut seen: HashSet<String> = HashSet::new();
    let mut out = Vec::with_capacity(entries.len());
    for entry in entries {
        let mut prefix = String::new();
        let comps: Vec<&str> = entry.path.split('/').collect();
        for comp in &comps[..comps.len().saturating_sub(1)] {
            if !prefix.is_empty() {
                prefix.push('/');
            }
            prefix.push_str(comp);
            if !explicit.contains(&prefix) && seen.insert(prefix.clone()) {
                out.push(dir_entry(&prefix));
            }
        }
        if entry.is_dir && !seen.insert(entry.path.clone()) {
            continue; // listed twice
        }
        out.push(entry);
    }
    out
}

fn dir_entry(path: &str) -> ArchiveEntry {
    ArchiveEntry {
        path: path.to_string(),
        size: 0,
        compressed_size: None,
        is_dir: true,
        modified: None,
    }
}

/// One file or folder to store, with the name it gets in the archive.
pub(super) struct SrcItem {
    pub path: PathBuf,
    pub name: String,
    pub is_dir: bool,
    pub size: u64,
    pub modified: Option<SystemTime>,
    pub mode: Option<u32>,
}

/// Walks the sources: each is stored under its own file name, recursively. Symlinks to
/// files are stored as files; other symlinks and special files are skipped. `exclude` (the
/// archive being written) is never stored in itself.
pub(super) fn collect_sources(sources: &[PathBuf], exclude: &[&Path]) -> io::Result<Vec<SrcItem>> {
    let exclude: Vec<PathBuf> = exclude
        .iter()
        .map(|p| std::path::absolute(p).unwrap_or_else(|_| p.to_path_buf()))
        .collect();
    let mut items = Vec::new();
    for src in sources {
        let base = src
            .file_name()
            .ok_or_else(|| invalid(format!("cannot archive {}", src.display())))?
            .to_string_lossy()
            .into_owned();
        for e in WalkDir::new(src).follow_links(false).sort_by_file_name() {
            let e = e?;
            let meta = if e.file_type().is_symlink() {
                match fs::metadata(e.path()) {
                    Ok(m) if m.is_file() => m,
                    _ => continue,
                }
            } else {
                e.metadata()?
            };
            if !(meta.is_dir() || meta.is_file()) {
                continue;
            }
            if std::path::absolute(e.path()).is_ok_and(|p| exclude.contains(&p)) {
                continue;
            }
            let rel = e.path().strip_prefix(src).unwrap_or(Path::new(""));
            let mut name = base.clone();
            for c in rel.components() {
                name.push('/');
                name.push_str(&c.as_os_str().to_string_lossy());
            }
            items.push(SrcItem {
                path: e.path().to_path_buf(),
                name,
                is_dir: meta.is_dir(),
                size: meta.len(),
                modified: meta.modified().ok(),
                mode: mode_of(&meta),
            });
        }
    }
    Ok(items)
}
