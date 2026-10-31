//! RAR, ARJ, LZH, CAB, ISO, … through an installed `7z` / `unrar` command line.

use std::ffi::OsString;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::SystemTime;

use chrono::{Local, NaiveDateTime};

use super::ArchiveEntry;
use super::common::{Plan, normalize, resolve, with_implied_dirs};

const TOOLS: [&str; 4] = ["7z", "7za", "7zz", "unrar"];

/// First of `7z`, `7za`, `7zz`, `unrar` found on `PATH`.
pub fn external_tool() -> Option<PathBuf> {
    let path = std::env::var_os("PATH")?;
    TOOLS.iter().find_map(|name| {
        std::env::split_paths(&path).find_map(|dir| {
            let plain = dir.join(name);
            let exe = dir.join(format!("{name}.exe"));
            [plain, exe].into_iter().find(|p| p.is_file())
        })
    })
}

fn is_unrar(tool: &Path) -> bool {
    tool.file_stem()
        .is_some_and(|s| s.to_string_lossy().to_lowercase().starts_with("unrar"))
}

fn missing_tool() -> io::Error {
    io::Error::new(
        io::ErrorKind::Unsupported,
        "this archive format needs an external tool: install 7-Zip (7z) or unrar and make sure it is on PATH",
    )
}

fn tool_or_err() -> io::Result<PathBuf> {
    external_tool().ok_or_else(missing_tool)
}

/// Runs `tool args…`, returning stdout; a failure carries the tool's own message.
fn run(tool: &Path, args: &[OsString]) -> io::Result<String> {
    let out = Command::new(tool)
        .args(args)
        .stdin(Stdio::null())
        .output()
        .map_err(|e| io::Error::new(e.kind(), format!("cannot run {}: {e}", tool.display())))?;
    if out.status.success() {
        return Ok(String::from_utf8_lossy(&out.stdout).into_owned());
    }
    let text = String::from_utf8_lossy(&out.stdout);
    let err = String::from_utf8_lossy(&out.stderr);
    let msg = [err.trim(), text.trim()]
        .into_iter()
        .find(|s| !s.is_empty())
        .unwrap_or("unknown error");
    Err(io::Error::other(format!(
        "{} failed ({}): {}",
        tool.display(),
        out.status,
        msg.lines().last().unwrap_or(msg)
    )))
}

fn os(args: &[&str]) -> Vec<OsString> {
    args.iter().map(OsString::from).collect()
}

pub(super) fn list(archive: &Path) -> io::Result<Vec<ArchiveEntry>> {
    list_with(&tool_or_err()?, archive)
}

pub(super) fn list_with(tool: &Path, archive: &Path) -> io::Result<Vec<ArchiveEntry>> {
    let unrar = is_unrar(tool);
    let mut args = if unrar {
        os(&["lb", "-p-", "--"])
    } else {
        os(&["l", "-slt", "--"])
    };
    args.push(archive.into());
    let text = run(tool, &args)?;
    Ok(with_implied_dirs(if unrar {
        parse_lb(&text)
    } else {
        parse_slt(&text)
    }))
}

fn parse_time(s: &str) -> Option<SystemTime> {
    let naive = NaiveDateTime::parse_from_str(s.get(..19)?, "%Y-%m-%d %H:%M:%S").ok()?;
    Some(naive.and_local_timezone(Local).earliest()?.into())
}

/// Parses `7z l -slt`: a header block, a `----------` line, then one block per entry.
pub(super) fn parse_slt(text: &str) -> Vec<ArchiveEntry> {
    let text = text.replace("\r\n", "\n");
    let Some((_, body)) = text.split_once("\n----------") else {
        return Vec::new();
    };
    let mut out = Vec::new();
    for block in body.split("\n\n") {
        let mut e = ArchiveEntry {
            path: String::new(),
            size: 0,
            compressed_size: None,
            is_dir: false,
            modified: None,
        };
        for line in block.lines() {
            let Some((key, val)) = line.split_once(" = ") else {
                continue;
            };
            let val = val.trim_end_matches('\r');
            match key.trim() {
                "Path" => e.path = normalize(val),
                "Size" => e.size = val.parse().unwrap_or(0),
                "Packed Size" => e.compressed_size = val.parse().ok(),
                "Modified" => e.modified = parse_time(val),
                "Folder" => e.is_dir = e.is_dir || val == "+",
                "Attributes" => e.is_dir = e.is_dir || val.starts_with('D'),
                _ => {}
            }
        }
        if !e.path.is_empty() {
            out.push(e);
        }
    }
    out
}

/// Parses `unrar lb` (bare names): a name that is another's parent is a folder.
pub(super) fn parse_lb(text: &str) -> Vec<ArchiveEntry> {
    let names: Vec<String> = text
        .lines()
        .map(|l| normalize(l.trim_end_matches('\r')))
        .filter(|n| !n.is_empty())
        .collect();
    names
        .iter()
        .map(|n| ArchiveEntry {
            path: n.clone(),
            size: 0,
            compressed_size: None,
            is_dir: names.iter().any(|o| {
                o.strip_prefix(n.as_str())
                    .is_some_and(|r| r.starts_with('/'))
            }),
            modified: None,
        })
        .collect()
}

fn extract_args(tool: &Path, archive: &Path, out: &Path, names: &[String]) -> Vec<OsString> {
    let mut args;
    if is_unrar(tool) {
        args = os(&["x", "-y", "-o+", "-p-", "--"]);
        args.push(archive.into());
        for n in names {
            args.push(n.into());
            args.push(format!("{n}/*").into());
        }
        let mut dest = out.as_os_str().to_os_string();
        dest.push(std::path::MAIN_SEPARATOR_STR);
        args.push(dest);
    } else {
        args = os(&["x", "-y"]);
        let mut o = OsString::from("-o");
        o.push(out);
        args.push(o);
        args.push("--".into());
        args.push(archive.into());
        args.extend(names.iter().map(OsString::from));
    }
    args
}

pub(super) fn extract(
    archive: &Path,
    plan: &Plan,
    dest: &Path,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<usize> {
    let tool = tool_or_err()?;
    extract_with(&tool, archive, plan, dest, progress)
}

pub(super) fn extract_with(
    tool: &Path,
    archive: &Path,
    plan: &Plan,
    dest: &Path,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<usize> {
    let entries = list_with(tool, archive)?;
    let count = entries
        .iter()
        .filter(|e| !e.is_dir && plan.wants(&e.path))
        .count();
    let name = archive
        .file_name()
        .map_or_else(String::new, |n| n.to_string_lossy().into_owned());
    if !progress(&name) {
        return Ok(0);
    }
    fs::create_dir_all(dest)?;
    let Some(selection) = plan.selection() else {
        run(tool, &extract_args(tool, archive, dest, &[]))?;
        return Ok(count);
    };
    // Extract into a scratch folder, then move the selection to its browsed-folder-relative place.
    let scratch = dest.join(format!(
        ".d3skcmd-extract-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(SystemTime::UNIX_EPOCH)
            .map_or(0, |d| d.as_nanos())
    ));
    fs::create_dir_all(&scratch)?;
    let result = run(tool, &extract_args(tool, archive, &scratch, selection))
        .and_then(|_| relocate(plan, selection, &scratch, dest));
    let _ = fs::remove_dir_all(&scratch);
    result.map(|()| count)
}

fn relocate(plan: &Plan, selection: &[String], scratch: &Path, dest: &Path) -> io::Result<()> {
    for sel in selection {
        let Some(target) = resolve(plan, dest, sel)? else {
            continue;
        };
        let from = sel.split('/').fold(scratch.to_path_buf(), |p, c| p.join(c));
        if fs::symlink_metadata(&from).is_ok() {
            merge_move(&from, &target)?;
        }
    }
    Ok(())
}

fn merge_move(from: &Path, to: &Path) -> io::Result<()> {
    if from.is_dir() {
        fs::create_dir_all(to)?;
        for entry in fs::read_dir(from)? {
            let entry = entry?;
            merge_move(&entry.path(), &to.join(entry.file_name()))?;
        }
        return Ok(());
    }
    if let Some(parent) = to.parent() {
        fs::create_dir_all(parent)?;
    }
    if fs::rename(from, to).is_err() {
        fs::copy(from, to)?;
    }
    Ok(())
}
