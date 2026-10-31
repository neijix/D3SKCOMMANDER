//! File time tools: shift (AddTime), incremental re-date (ReDate) and plain set (touch).

use std::fs;
use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime};

use filetime::FileTime;

/// Result of one file: its path and `Ok(())` or a readable message.
pub type FileResult = (PathBuf, Result<(), String>);

fn describe(path: &Path, e: &dyn std::fmt::Display) -> String {
    format!("{}: {e}", path.display())
}

/// `t` moved by `secs` (negative = earlier).
fn shifted(t: SystemTime, secs: i64) -> Result<SystemTime, String> {
    let d = Duration::from_secs(secs.unsigned_abs());
    let out = if secs >= 0 {
        t.checked_add(d)
    } else {
        t.checked_sub(d)
    };
    out.ok_or_else(|| "resulting time is out of range".to_string())
}

fn shift_one(path: &Path, offset_secs: i64, modified: bool, accessed: bool) -> Result<(), String> {
    let meta = fs::metadata(path).map_err(|e| describe(path, &e))?;
    let mut mtime = meta.modified().map_err(|e| describe(path, &e))?;
    let mut atime = meta.accessed().unwrap_or(mtime);
    if modified {
        mtime = shifted(mtime, offset_secs)?;
    }
    if accessed {
        atime = shifted(atime, offset_secs)?;
    }
    filetime::set_file_times(
        path,
        FileTime::from_system_time(atime),
        FileTime::from_system_time(mtime),
    )
    .map_err(|e| describe(path, &e))
}

/// AddTime: move the modification and/or access time of each file by `offset_secs`.
pub fn shift_times(
    files: &[PathBuf],
    offset_secs: i64,
    modified: bool,
    accessed: bool,
) -> Vec<FileResult> {
    files
        .iter()
        .map(|p| (p.clone(), shift_one(p, offset_secs, modified, accessed)))
        .collect()
}

/// ReDate: the i-th file gets modification time `start + i * step_secs`.
pub fn redate(files: &[PathBuf], start: SystemTime, step_secs: i64) -> Vec<FileResult> {
    files
        .iter()
        .enumerate()
        .map(|(i, p)| {
            let result = i64::try_from(i)
                .ok()
                .and_then(|i| i.checked_mul(step_secs))
                .ok_or_else(|| "step is out of range".to_string())
                .and_then(|off| shifted(start, off))
                .and_then(|t| set_one(p, Some(t), None));
            (p.clone(), result)
        })
        .collect()
}

fn set_one(
    path: &Path,
    modified: Option<SystemTime>,
    accessed: Option<SystemTime>,
) -> Result<(), String> {
    if modified.is_none() && accessed.is_none() {
        return Ok(());
    }
    let meta = fs::metadata(path).map_err(|e| describe(path, &e))?;
    let current_m = FileTime::from_last_modification_time(&meta);
    let current_a = FileTime::from_last_access_time(&meta);
    let m = modified.map_or(current_m, FileTime::from_system_time);
    let a = accessed.map_or(current_a, FileTime::from_system_time);
    filetime::set_file_times(path, a, m).map_err(|e| describe(path, &e))
}

/// Set the modification and/or access time of each file (`None` keeps the current one).
pub fn set_times(
    files: &[PathBuf],
    modified: Option<SystemTime>,
    accessed: Option<SystemTime>,
) -> Vec<FileResult> {
    files
        .iter()
        .map(|p| (p.clone(), set_one(p, modified, accessed)))
        .collect()
}

/// Parse an offset in seconds: `+1h30m`, `-2d`, `90s`, `1:30:00` (h:m:s), `-0:05` (h:m),
/// a bare number is seconds. Units: `w d h m s`; a leading `+`/`-` sets the sign.
pub fn parse_offset(s: &str) -> Result<i64, String> {
    let t = s.trim();
    let (neg, body) = match t.chars().next() {
        Some('-') => (true, &t[1..]),
        Some('+') => (false, &t[1..]),
        _ => (false, t),
    };
    let body = body.trim();
    if body.is_empty() {
        return Err(format!("empty time offset: {s:?}"));
    }
    let secs = if body.contains(':') {
        parse_clock(body)?
    } else {
        parse_units(body)?
    };
    let secs = i64::try_from(secs).map_err(|_| format!("time offset too large: {s:?}"))?;
    Ok(if neg { -secs } else { secs })
}

fn parse_clock(body: &str) -> Result<u64, String> {
    let parts: Vec<&str> = body.split(':').collect();
    if parts.len() > 3 {
        return Err(format!("invalid time offset: {body:?}"));
    }
    let mut nums = [0u64; 3];
    for (i, p) in parts.iter().enumerate() {
        nums[i] = p
            .trim()
            .parse::<u64>()
            .map_err(|_| format!("invalid time offset: {body:?}"))?;
    }
    // h:m or h:m:s
    Ok(nums[0]
        .saturating_mul(3600)
        .saturating_add(nums[1].saturating_mul(60))
        .saturating_add(nums[2]))
}

fn parse_units(body: &str) -> Result<u64, String> {
    let invalid = || format!("invalid time offset: {body:?}");
    let mut total: u64 = 0;
    let mut digits = String::new();
    let mut seen = false;
    for c in body.chars().chain(std::iter::once('\0')) {
        if c.is_ascii_digit() {
            digits.push(c);
            continue;
        }
        if digits.is_empty() && (c.is_whitespace() || c == '\0') {
            if c == '\0' {
                break;
            }
            continue;
        }
        let unit: u64 = match c.to_ascii_lowercase() {
            'w' => 604_800,
            'd' => 86_400,
            'h' => 3600,
            'm' => 60,
            's' => 1,
            '\0' => 1, // trailing bare number = seconds
            _ => return Err(invalid()),
        };
        if digits.is_empty() {
            return Err(invalid());
        }
        let n: u64 = digits.parse().map_err(|_| invalid())?;
        total = total.saturating_add(n.saturating_mul(unit));
        digits.clear();
        seen = true;
    }
    if seen { Ok(total) } else { Err(invalid()) }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(secs: u64) -> SystemTime {
        SystemTime::UNIX_EPOCH + Duration::from_secs(secs)
    }

    fn mtime(p: &Path) -> SystemTime {
        fs::metadata(p).unwrap().modified().unwrap()
    }

    fn make(dir: &Path, name: &str, secs: u64) -> PathBuf {
        let p = dir.join(name);
        fs::write(&p, b"x").unwrap();
        filetime::set_file_times(
            &p,
            FileTime::from_system_time(at(secs)),
            FileTime::from_system_time(at(secs)),
        )
        .unwrap();
        p
    }

    #[test]
    fn shift_moves_modified_only() {
        let dir = tempfile::tempdir().unwrap();
        let p = make(dir.path(), "a.txt", 1_000_000);
        let r = shift_times(std::slice::from_ref(&p), 3600, true, false);
        assert!(r[0].1.is_ok());
        assert_eq!(mtime(&p), at(1_003_600));
        let atime = FileTime::from_last_access_time(&fs::metadata(&p).unwrap());
        assert_eq!(atime.unix_seconds(), 1_000_000);
    }

    #[test]
    fn shift_negative_and_accessed() {
        let dir = tempfile::tempdir().unwrap();
        let p = make(dir.path(), "a.txt", 1_000_000);
        shift_times(std::slice::from_ref(&p), -60, true, true);
        assert_eq!(mtime(&p), at(999_940));
        let atime = FileTime::from_last_access_time(&fs::metadata(&p).unwrap());
        assert_eq!(atime.unix_seconds(), 999_940);
    }

    #[test]
    fn shift_missing_file_reports_error() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("nope");
        let r = shift_times(&[p], 10, true, true);
        assert!(r[0].1.is_err());
    }

    #[test]
    fn redate_steps_each_file() {
        let dir = tempfile::tempdir().unwrap();
        let files: Vec<PathBuf> = (0..3)
            .map(|i| make(dir.path(), &format!("f{i}"), 5))
            .collect();
        let r = redate(&files, at(2_000_000), 90);
        assert!(r.iter().all(|(_, x)| x.is_ok()));
        assert_eq!(mtime(&files[0]), at(2_000_000));
        assert_eq!(mtime(&files[1]), at(2_000_090));
        assert_eq!(mtime(&files[2]), at(2_000_180));
    }

    #[test]
    fn redate_negative_step() {
        let dir = tempfile::tempdir().unwrap();
        let files: Vec<PathBuf> = (0..2)
            .map(|i| make(dir.path(), &format!("f{i}"), 5))
            .collect();
        redate(&files, at(2_000_000), -10);
        assert_eq!(mtime(&files[1]), at(1_999_990));
    }

    #[test]
    fn set_times_sets_and_keeps() {
        let dir = tempfile::tempdir().unwrap();
        let p = make(dir.path(), "a.txt", 1_000_000);
        set_times(std::slice::from_ref(&p), Some(at(3_000_000)), None);
        assert_eq!(mtime(&p), at(3_000_000));
        let atime = FileTime::from_last_access_time(&fs::metadata(&p).unwrap());
        assert_eq!(atime.unix_seconds(), 1_000_000);
        set_times(std::slice::from_ref(&p), None, Some(at(4_000_000)));
        assert_eq!(mtime(&p), at(3_000_000));
        let atime = FileTime::from_last_access_time(&fs::metadata(&p).unwrap());
        assert_eq!(atime.unix_seconds(), 4_000_000);
    }

    #[test]
    fn parse_offset_forms() {
        assert_eq!(parse_offset("+1h30m"), Ok(5400));
        assert_eq!(parse_offset("-2d"), Ok(-172_800));
        assert_eq!(parse_offset("90s"), Ok(90));
        assert_eq!(parse_offset("90"), Ok(90));
        assert_eq!(parse_offset("1:30:00"), Ok(5400));
        assert_eq!(parse_offset("-0:05"), Ok(-300));
        assert_eq!(parse_offset("1w"), Ok(604_800));
        assert_eq!(parse_offset(" 1d 2h "), Ok(93_600));
        assert_eq!(parse_offset("+10M"), Ok(600));
    }

    #[test]
    fn parse_offset_rejects_garbage() {
        for bad in ["", "+", "abc", "1x", "h", "1:2:3:4", "1:x", "--5"] {
            assert!(parse_offset(bad).is_err(), "{bad:?} should fail");
        }
    }
}
