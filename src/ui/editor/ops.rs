//! Pure parts of the built-in editor (also used by the compare editor): line endings,
//! search and replace, file encoding round trip and atomic saving.

use std::io::{self, Write};
use std::ops::Range;
use std::path::Path;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum LineEnding {
    Lf,
    Crlf,
}

impl LineEnding {
    pub fn label(self) -> &'static str {
        match self {
            LineEnding::Lf => "LF",
            LineEnding::Crlf => "CRLF",
        }
    }
}

/// CRLF when most line endings of `text` are CRLF, LF otherwise (also for a text without any).
pub fn detect_line_ending(text: &str) -> LineEnding {
    let lf = text.matches('\n').count();
    let crlf = text.matches("\r\n").count();
    if crlf > 0 && crlf * 2 > lf {
        LineEnding::Crlf
    } else {
        LineEnding::Lf
    }
}

/// The editing buffer form: every `\r\n` becomes `\n`.
pub fn to_lf(text: &str) -> String {
    if text.contains("\r\n") {
        text.replace("\r\n", "\n")
    } else {
        text.to_string()
    }
}

/// Gives an LF-only buffer back the line endings it was loaded with.
pub fn restore_line_ending(text: &str, eol: LineEnding) -> String {
    match eol {
        LineEnding::Lf => text.to_string(),
        LineEnding::Crlf => to_lf(text).replace('\n', "\r\n"),
    }
}

/// What a file looks like once loaded for editing.
#[derive(Debug, PartialEq, Eq)]
pub struct Decoded {
    /// LF-only text.
    pub text: String,
    pub eol: LineEnding,
    pub bom: bool,
    /// The file held invalid UTF-8 (replaced by U+FFFD; saving rewrites those bytes).
    pub lossy: bool,
}

pub fn decode_file(bytes: &[u8]) -> Decoded {
    let (body, bom) = match bytes.strip_prefix(&[0xEF, 0xBB, 0xBF]) {
        Some(rest) => (rest, true),
        None => (bytes, false),
    };
    let lossy = std::str::from_utf8(body).is_err();
    let raw = String::from_utf8_lossy(body);
    Decoded {
        eol: detect_line_ending(&raw),
        text: to_lf(&raw),
        bom,
        lossy,
    }
}

pub fn encode_file(text: &str, eol: LineEnding, bom: bool) -> Vec<u8> {
    let body = restore_line_ending(text, eol);
    let mut out = Vec::with_capacity(body.len() + 3);
    if bom {
        out.extend_from_slice(&[0xEF, 0xBB, 0xBF]);
    }
    out.extend_from_slice(body.as_bytes());
    out
}

fn chars_equal(a: char, b: char, case: bool) -> bool {
    a == b || (!case && a.to_lowercase().eq(b.to_lowercase()))
}

/// Byte length of the prefix of `hay` that equals `needle` (per-character case folding when
/// `case` is false), `None` when `hay` does not start with it.
pub fn match_len_at(hay: &str, needle: &str, case: bool) -> Option<usize> {
    if needle.is_empty() {
        return None;
    }
    let mut h = hay.char_indices();
    for n in needle.chars() {
        let (_, c) = h.next()?;
        if !chars_equal(c, n, case) {
            return None;
        }
    }
    Some(h.next().map_or(hay.len(), |(i, _)| i))
}

/// First match of `needle` at or after byte `from`, wrapping to the start of the text.
pub fn find_next(text: &str, needle: &str, case: bool, from: usize) -> Option<Range<usize>> {
    if needle.is_empty() {
        return None;
    }
    let mut from = from.min(text.len());
    while !text.is_char_boundary(from) {
        from += 1;
    }
    let scan = |range: Range<usize>| {
        text[range.clone()].char_indices().find_map(|(i, _)| {
            let start = range.start + i;
            match_len_at(&text[start..], needle, case).map(|len| start..start + len)
        })
    };
    scan(from..text.len()).or_else(|| scan(0..from))
}

/// Replaces every occurrence of `find`; returns the new text and the replacement count.
pub fn replace_all(text: &str, find: &str, with: &str, case: bool) -> (String, usize) {
    if find.is_empty() {
        return (text.to_string(), 0);
    }
    let mut out = String::with_capacity(text.len());
    let mut count = 0;
    let mut i = 0;
    while i < text.len() {
        if let Some(len) = match_len_at(&text[i..], find, case) {
            out.push_str(with);
            count += 1;
            i += len;
        } else {
            let c = text[i..].chars().next().map_or(1, char::len_utf8);
            out.push_str(&text[i..i + c]);
            i += c;
        }
    }
    (out, count)
}

/// Writes `bytes` to a temporary file next to `path`, then renames it over `path`.
pub fn write_atomic(path: &Path, bytes: &[u8]) -> io::Result<()> {
    let name = path
        .file_name()
        .ok_or_else(|| io::Error::new(io::ErrorKind::InvalidInput, "no file name"))?;
    let mut tmp_name = std::ffi::OsString::from(".");
    tmp_name.push(name);
    tmp_name.push(".d3sk-tmp");
    let tmp = path.with_file_name(tmp_name);
    let result = (|| {
        let mut f = std::fs::File::create(&tmp)?;
        f.write_all(bytes)?;
        f.sync_all()?;
        drop(f);
        if let Ok(meta) = std::fs::metadata(path) {
            let _ = std::fs::set_permissions(&tmp, meta.permissions());
        }
        std::fs::rename(&tmp, path)
    })();
    if result.is_err() {
        let _ = std::fs::remove_file(&tmp);
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_line_endings() {
        assert_eq!(detect_line_ending("a\r\nb\r\n"), LineEnding::Crlf);
        assert_eq!(detect_line_ending("a\nb\n"), LineEnding::Lf);
        assert_eq!(detect_line_ending("no newline"), LineEnding::Lf);
        assert_eq!(detect_line_ending("a\r\nb\nc\nd\n"), LineEnding::Lf);
    }

    #[test]
    fn crlf_round_trip() {
        let src = "one\r\ntwo\r\n\r\nthree";
        let d = decode_file(src.as_bytes());
        assert_eq!(d.text, "one\ntwo\n\nthree");
        assert_eq!(d.eol, LineEnding::Crlf);
        assert_eq!(encode_file(&d.text, d.eol, d.bom), src.as_bytes());
    }

    #[test]
    fn restore_does_not_double_carriage_returns() {
        assert_eq!(
            restore_line_ending("a\r\nb\nc", LineEnding::Crlf),
            "a\r\nb\r\nc"
        );
        assert_eq!(restore_line_ending("a\nb", LineEnding::Lf), "a\nb");
    }

    #[test]
    fn bom_and_invalid_utf8() {
        let d = decode_file(b"\xEF\xBB\xBFhi\n");
        assert!(d.bom && !d.lossy);
        assert_eq!(encode_file(&d.text, d.eol, d.bom), b"\xEF\xBB\xBFhi\n");
        let bad = decode_file(b"a\xFFb");
        assert!(bad.lossy);
        assert_eq!(bad.text, "a\u{FFFD}b");
    }

    #[test]
    fn replace_all_counts_and_folds_case() {
        assert_eq!(
            replace_all("aXa xa", "xa", "-", true),
            ("aXa -".to_string(), 1)
        );
        assert_eq!(
            replace_all("aXa xa", "xa", "-", false),
            ("a- -".to_string(), 2)
        );
        assert_eq!(replace_all("abc", "", "-", false), ("abc".to_string(), 0));
        assert_eq!(replace_all("é É", "é", "e", false), ("e e".to_string(), 2));
        assert_eq!(replace_all("aaa", "aa", "b", true), ("ba".to_string(), 1));
    }

    #[test]
    fn find_next_wraps_around() {
        let t = "foo Bar foo";
        assert_eq!(find_next(t, "foo", true, 0), Some(0..3));
        assert_eq!(find_next(t, "foo", true, 1), Some(8..11));
        assert_eq!(find_next(t, "foo", true, 9), Some(0..3));
        assert_eq!(find_next(t, "bar", true, 0), None);
        assert_eq!(find_next(t, "bar", false, 0), Some(4..7));
        assert_eq!(find_next("é-é", "é", true, 1), Some(3..5));
    }

    #[test]
    fn atomic_write_replaces_and_leaves_no_temp() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("f.txt");
        std::fs::write(&p, "old").unwrap();
        write_atomic(&p, b"new").unwrap();
        assert_eq!(std::fs::read(&p).unwrap(), b"new");
        let q = dir.path().join("fresh.txt");
        write_atomic(&q, b"x").unwrap();
        assert_eq!(std::fs::read(&q).unwrap(), b"x");
        let names = std::fs::read_dir(dir.path()).unwrap().count();
        assert_eq!(names, 2);
    }
}
