//! Parsing of directory listings: MLSD facts (RFC 3659) and the two `LIST` dialects seen in
//! the wild, unix `ls -l` and DOS/IIS.

use std::time::{Duration, SystemTime, UNIX_EPOCH};

use chrono::{Datelike, NaiveDate, NaiveDateTime, NaiveTime, Utc};

use super::RemoteEntry;

/// Parse one line of a `LIST` reply (unix or DOS/IIS format). `None` for lines that are not
/// an entry (`total 12`, blank lines, `.` and `..`).
pub fn parse_list_line(line: &str) -> Option<RemoteEntry> {
    parse_list_line_at(line, Utc::now().date_naive())
}

/// [`parse_list_line`] with an explicit "today", to resolve the year of unix lines that
/// only carry a time (`Jan 01 12:00`: the most recent such date not in the future).
fn parse_list_line_at(line: &str, today: NaiveDate) -> Option<RemoteEntry> {
    let line = line.trim_end_matches(['\r', '\n']);
    let entry = if line.trim_start().starts_with(|c: char| c.is_ascii_digit()) {
        parse_dos(line)
    } else {
        parse_unix(line, today)
    }?;
    (entry.name != "." && entry.name != "..").then_some(entry)
}

/// Next whitespace-delimited token and the rest of the string after the whitespace run.
fn token(s: &str) -> Option<(&str, &str)> {
    let s = s.trim_start();
    if s.is_empty() {
        return None;
    }
    let end = s.find(char::is_whitespace).unwrap_or(s.len());
    Some((&s[..end], s[end..].trim_start()))
}

fn to_system_time(dt: NaiveDateTime) -> SystemTime {
    let secs = dt.and_utc().timestamp();
    match u64::try_from(secs) {
        Ok(s) => UNIX_EPOCH + Duration::from_secs(s),
        Err(_) => UNIX_EPOCH - Duration::from_secs(secs.unsigned_abs()),
    }
}

fn month_number(name: &str) -> Option<u32> {
    const MONTHS: [&str; 12] = [
        "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec",
    ];
    let lower = name.to_ascii_lowercase();
    MONTHS
        .iter()
        .position(|m| *m == lower)
        .map(|i| i as u32 + 1)
}

fn parse_unix(line: &str, today: NaiveDate) -> Option<RemoteEntry> {
    let (perms, rest) = token(line)?;
    let kind = perms.chars().next()?;
    if !matches!(kind, 'd' | '-' | 'l' | 'b' | 'c' | 'p' | 's') || perms.len() < 10 {
        return None;
    }
    let (_links, rest) = token(rest)?;
    let (_owner, rest) = token(rest)?;
    // Some servers omit the group column: the size is then the only number before the month.
    let (mut size_tok, mut rest) = token(rest)?;
    let mut month = token(rest);
    if month.is_some_and(|(m, _)| month_number(m).is_none()) {
        // `size_tok` was the group; the real size follows.
        let (s, r) = month?;
        size_tok = s;
        rest = r;
        month = token(rest);
    }
    let (month_tok, rest) = month?;
    let (day_tok, rest) = token(rest)?;
    let (when_tok, name_part) = token(rest)?;
    let size: u64 = size_tok.parse().ok()?;
    let modified = unix_date(month_tok, day_tok, when_tok, today);
    let mut name = name_part;
    if kind == 'l'
        && let Some((n, _target)) = name.split_once(" -> ")
    {
        name = n;
    }
    if name.is_empty() {
        return None;
    }
    Some(RemoteEntry {
        name: name.to_string(),
        is_dir: kind == 'd',
        size,
        modified,
        permissions: perms.to_string(),
    })
}

fn unix_date(month: &str, day: &str, when: &str, today: NaiveDate) -> Option<SystemTime> {
    let month = month_number(month)?;
    let day: u32 = day.parse().ok()?;
    let (year, time) = match when.split_once(':') {
        Some((h, m)) => {
            let time = NaiveTime::from_hms_opt(h.parse().ok()?, m.parse().ok()?, 0)?;
            let this_year = NaiveDate::from_ymd_opt(today.year(), month, day);
            // `ls` shows the time instead of the year for dates within the last ~6 months.
            let year = match this_year {
                Some(d) if d <= today + chrono::Days::new(1) => today.year(),
                _ => today.year() - 1,
            };
            (year, time)
        }
        None => (when.parse().ok()?, NaiveTime::MIN),
    };
    let date = NaiveDate::from_ymd_opt(year, month, day)?;
    Some(to_system_time(date.and_time(time)))
}

fn parse_dos(line: &str) -> Option<RemoteEntry> {
    let (date_tok, rest) = token(line)?;
    let (time_tok, rest) = token(rest)?;
    let (size_tok, name) = token(rest)?;
    if name.is_empty() {
        return None;
    }
    let is_dir = size_tok.eq_ignore_ascii_case("<DIR>");
    let size = if is_dir {
        0
    } else {
        size_tok.replace(',', "").parse().ok()?
    };
    Some(RemoteEntry {
        name: name.to_string(),
        is_dir,
        size,
        modified: dos_date(date_tok, time_tok),
        permissions: String::new(),
    })
}

fn dos_date(date: &str, time: &str) -> Option<SystemTime> {
    let mut parts = date.split(['-', '/']);
    let month: u32 = parts.next()?.parse().ok()?;
    let day: u32 = parts.next()?.parse().ok()?;
    let mut year: i32 = parts.next()?.parse().ok()?;
    if year < 100 {
        year += if year < 70 { 2000 } else { 1900 };
    }
    let upper = time.to_ascii_uppercase();
    let (clock, pm) = match upper.strip_suffix("PM") {
        Some(c) => (c, Some(true)),
        None => (
            upper.strip_suffix("AM").unwrap_or(&upper),
            upper.ends_with("AM").then_some(false),
        ),
    };
    let (h, m) = clock.split_once(':')?;
    let mut hour: u32 = h.parse().ok()?;
    match pm {
        Some(true) if hour < 12 => hour += 12,
        Some(false) if hour == 12 => hour = 0,
        _ => {}
    }
    let date = NaiveDate::from_ymd_opt(year, month, day)?;
    Some(to_system_time(date.and_time(NaiveTime::from_hms_opt(
        hour,
        m.parse().ok()?,
        0,
    )?)))
}

/// Parse one MLSD line: `fact=value;fact=value; name`. `None` for the `.`/`..` entries
/// (`cdir`, `pdir`) and malformed lines.
pub fn parse_mlsd_line(line: &str) -> Option<RemoteEntry> {
    let line = line.trim_end_matches(['\r', '\n']);
    let (facts, name) = line.split_once(' ')?;
    if name.is_empty() {
        return None;
    }
    let mut entry = RemoteEntry {
        name: name.to_string(),
        is_dir: false,
        size: 0,
        modified: None,
        permissions: String::new(),
    };
    for fact in facts.split(';').filter(|f| !f.is_empty()) {
        let (key, value) = fact.split_once('=')?;
        match key.to_ascii_lowercase().as_str() {
            "type" => match value.to_ascii_lowercase().as_str() {
                "cdir" | "pdir" => return None,
                "dir" => entry.is_dir = true,
                _ => {}
            },
            "size" | "sizd" => entry.size = value.parse().unwrap_or(0),
            "modify" => entry.modified = mlsd_time(value),
            "unix.mode" => entry.permissions = value.to_string(),
            _ => {}
        }
    }
    Some(entry)
}

/// `YYYYMMDDHHMMSS[.fff]`, UTC.
fn mlsd_time(value: &str) -> Option<SystemTime> {
    let digits = value.split('.').next()?;
    let dt = NaiveDateTime::parse_from_str(digits, "%Y%m%d%H%M%S").ok()?;
    Some(to_system_time(dt))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(secs: u64) -> SystemTime {
        UNIX_EPOCH + Duration::from_secs(secs)
    }

    fn today() -> NaiveDate {
        NaiveDate::from_ymd_opt(2026, 10, 31).unwrap()
    }

    fn unix(line: &str) -> Option<RemoteEntry> {
        parse_list_line_at(line, today())
    }

    #[test]
    fn unix_directory() {
        let e = unix("drwxr-xr-x 2 user group 4096 Jan 01 12:00 dir").unwrap();
        assert_eq!(e.name, "dir");
        assert!(e.is_dir);
        assert_eq!(e.size, 4096);
        assert_eq!(e.permissions, "drwxr-xr-x");
        // 2026-01-01 12:00 UTC
        assert_eq!(e.modified, Some(at(1_767_268_800)));
    }

    #[test]
    fn unix_file_with_year_and_spaces() {
        let e =
            unix("-rw-r--r--   1 ftp      ftp        123456 Dec  7  2019 my file (1).txt").unwrap();
        assert_eq!(e.name, "my file (1).txt");
        assert!(!e.is_dir);
        assert_eq!(e.size, 123_456);
        assert_eq!(e.modified, Some(at(1_575_676_800)));
    }

    #[test]
    fn unix_time_form_in_the_future_means_last_year() {
        // "Dec 25 10:00" listed on Oct 31 2026 is December 2025.
        let e = unix("-rw-r--r-- 1 u g 10 Dec 25 10:00 old.txt").unwrap();
        let expected = to_system_time(
            NaiveDate::from_ymd_opt(2025, 12, 25)
                .unwrap()
                .and_hms_opt(10, 0, 0)
                .unwrap(),
        );
        assert_eq!(e.modified, Some(expected));
    }

    #[test]
    fn unix_symlink_keeps_its_own_name() {
        let e = unix("lrwxrwxrwx 1 user group 1 Jan 01 12:00 a -> b").unwrap();
        assert_eq!(e.name, "a");
        assert!(!e.is_dir);
    }

    #[test]
    fn unix_without_group_column() {
        let e = unix("-rw-r--r-- 1 owner 77 Mar 03 2020 f.bin").unwrap();
        assert_eq!(e.name, "f.bin");
        assert_eq!(e.size, 77);
    }

    #[test]
    fn dos_directory_and_file() {
        let d = unix("01-01-24  12:00PM       <DIR>          dir").unwrap();
        assert_eq!(d.name, "dir");
        assert!(d.is_dir);
        assert_eq!(d.size, 0);
        assert_eq!(d.modified, Some(at(1_704_110_400)));
        let f = unix("01-01-24  12:00PM 1234 file.txt").unwrap();
        assert_eq!(f.name, "file.txt");
        assert!(!f.is_dir);
        assert_eq!(f.size, 1234);
        let am = unix("12-31-99  12:05AM 5 old file.txt").unwrap();
        assert_eq!(am.name, "old file.txt");
        assert_eq!(am.modified, Some(at(946_598_700)));
    }

    #[test]
    fn non_entries_are_skipped() {
        assert!(unix("total 12").is_none());
        assert!(unix("").is_none());
        assert!(unix("drwxr-xr-x 2 user group 4096 Jan 01 12:00 .").is_none());
        assert!(unix("drwxr-xr-x 2 user group 4096 Jan 01 12:00 ..").is_none());
        assert!(unix("garbage line here").is_none());
    }

    #[test]
    fn trailing_cr_is_ignored() {
        let e = unix("-rw-r--r-- 1 u g 1 Jan 01 2020 a.txt\r").unwrap();
        assert_eq!(e.name, "a.txt");
    }

    #[test]
    fn mlsd_entries() {
        let f =
            parse_mlsd_line("type=file;size=1024;modify=20240101120000;perm=r; a b.txt").unwrap();
        assert_eq!(f.name, "a b.txt");
        assert!(!f.is_dir);
        assert_eq!(f.size, 1024);
        assert_eq!(f.modified, Some(at(1_704_110_400)));
        let d = parse_mlsd_line("Type=DIR;Modify=20240101120000.123;UNIX.mode=0755; sub").unwrap();
        assert!(d.is_dir);
        assert_eq!(d.permissions, "0755");
        assert_eq!(d.modified, Some(at(1_704_110_400)));
        assert!(parse_mlsd_line("type=cdir;modify=20240101120000; .").is_none());
        assert!(parse_mlsd_line("type=pdir;modify=20240101120000; ..").is_none());
    }
}
