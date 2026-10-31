//! Pure helpers of the lister: encoding detection and decoding, line splitting and wrapping,
//! hex rows and case-(in)sensitive search over lines and bytes.

use std::fmt;
use std::ops::Range;

/// Bytes the lister reads at most; a larger file shows a "truncated" note.
pub const MAX_READ: usize = 16 * 1024 * 1024;
/// A NUL byte in this many leading bytes marks the file as binary.
pub const SNIFF: usize = 8 * 1024;
/// Bytes per hex row.
pub const HEX_WIDTH: usize = 16;
/// Unwrapped lines longer than this are cut for display (one huge line stays smooth).
pub const MAX_LINE_CHARS: usize = 2000;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Encoding {
    Utf8,
    Utf8Bom,
    Utf16Le,
    Utf16Be,
}

impl fmt::Display for Encoding {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Encoding::Utf8 => "UTF-8",
            Encoding::Utf8Bom => "UTF-8 BOM",
            Encoding::Utf16Le => "UTF-16 LE",
            Encoding::Utf16Be => "UTF-16 BE",
        })
    }
}

/// The encoding announced by a byte order mark (UTF-8 otherwise) and the BOM's length.
pub fn detect_encoding(bytes: &[u8]) -> (Encoding, usize) {
    if bytes.starts_with(&[0xEF, 0xBB, 0xBF]) {
        (Encoding::Utf8Bom, 3)
    } else if bytes.starts_with(&[0xFF, 0xFE]) {
        (Encoding::Utf16Le, 2)
    } else if bytes.starts_with(&[0xFE, 0xFF]) {
        (Encoding::Utf16Be, 2)
    } else {
        (Encoding::Utf8, 0)
    }
}

/// True for data that is not text: a NUL in the first `SNIFF` bytes, unless a UTF-16 BOM
/// explains the NULs.
pub fn is_binary(bytes: &[u8]) -> bool {
    let (enc, _) = detect_encoding(bytes);
    matches!(enc, Encoding::Utf8 | Encoding::Utf8Bom) && bytes.iter().take(SNIFF).any(|&b| b == 0)
}

/// Decodes `bytes` (BOM included) to a string; invalid sequences become U+FFFD and tabs
/// become four spaces, so a monospace column count equals a character count.
pub fn decode(bytes: &[u8]) -> (String, Encoding) {
    let (enc, skip) = detect_encoding(bytes);
    let body = &bytes[skip..];
    let text = match enc {
        Encoding::Utf8 | Encoding::Utf8Bom => String::from_utf8_lossy(body).into_owned(),
        Encoding::Utf16Le => decode_utf16(body, u16::from_le_bytes),
        Encoding::Utf16Be => decode_utf16(body, u16::from_be_bytes),
    };
    let text = if text.contains('\t') {
        text.replace('\t', "    ")
    } else {
        text
    };
    (text, enc)
}

fn decode_utf16(body: &[u8], unit: fn([u8; 2]) -> u16) -> String {
    let units = body.as_chunks::<2>().0.iter().map(|&c| unit(c));
    char::decode_utf16(units)
        .map(|r| r.unwrap_or(char::REPLACEMENT_CHARACTER))
        .collect()
}

/// Byte ranges of the lines of `text`, without the line terminator (`\n` or `\r\n`).
pub fn line_ranges(text: &str) -> Vec<Range<usize>> {
    let mut out = Vec::new();
    let mut start = 0;
    for (i, b) in text.bytes().enumerate() {
        if b == b'\n' {
            let end = if i > start && text.as_bytes()[i - 1] == b'\r' {
                i - 1
            } else {
                i
            };
            out.push(start..end);
            start = i + 1;
        }
    }
    if start < text.len() {
        out.push(start..text.len());
    }
    out
}

/// One display row of a wrapped text: a slice of line `line`.
#[derive(Clone, PartialEq, Eq, Debug)]
pub struct VisualRow {
    pub line: usize,
    pub range: Range<usize>,
    /// First row of its line (carries the line number).
    pub first: bool,
}

/// Splits every line into rows of at most `cols` characters.
pub fn wrap_rows(text: &str, lines: &[Range<usize>], cols: usize) -> Vec<VisualRow> {
    let cols = cols.max(1);
    let mut rows = Vec::with_capacity(lines.len());
    for (line, r) in lines.iter().enumerate() {
        let s = &text[r.clone()];
        if s.chars().nth(cols).is_none() {
            rows.push(VisualRow {
                line,
                range: r.clone(),
                first: true,
            });
            continue;
        }
        let mut start = 0;
        let mut count = 0;
        let mut first = true;
        for (i, _) in s.char_indices() {
            if count == cols {
                rows.push(VisualRow {
                    line,
                    range: r.start + start..r.start + i,
                    first,
                });
                first = false;
                start = i;
                count = 0;
            }
            count += 1;
        }
        rows.push(VisualRow {
            line,
            range: r.start + start..r.end,
            first,
        });
    }
    rows
}

/// The part of a line shown without wrapping.
pub fn clip_line(s: &str) -> std::borrow::Cow<'_, str> {
    match s.char_indices().nth(MAX_LINE_CHARS) {
        Some((i, _)) => format!("{} …", &s[..i]).into(),
        None => s.into(),
    }
}

/// `OFFSET  hh hh … hh  |ascii|` for up to 16 bytes.
pub fn format_hex_row(offset: usize, chunk: &[u8]) -> String {
    let mut s = format!("{offset:08X} ");
    for i in 0..HEX_WIDTH {
        if i % 8 == 0 {
            s.push(' ');
        }
        match chunk.get(i) {
            Some(b) => s.push_str(&format!("{b:02X} ")),
            None => s.push_str("   "),
        }
    }
    s.push_str(" |");
    s.extend(chunk.iter().map(|&b| {
        if (0x20..0x7F).contains(&b) {
            b as char
        } else {
            '.'
        }
    }));
    s.push('|');
    s
}

/// Searches the lines for `query` starting at line `from` (inclusive), wrapping around.
/// `forward = false` walks backwards.
pub fn find_line(
    text: &str,
    lines: &[Range<usize>],
    query: &str,
    case: bool,
    from: usize,
    forward: bool,
) -> Option<usize> {
    let n = lines.len();
    if n == 0 || query.is_empty() {
        return None;
    }
    let lowered = query.to_lowercase();
    let hit = |i: usize| {
        let line = &text[lines[i].clone()];
        if case {
            line.contains(query)
        } else {
            line.to_lowercase().contains(&lowered)
        }
    };
    let from = from % n;
    (0..n)
        .map(|k| {
            if forward {
                (from + k) % n
            } else {
                (from + n - k) % n
            }
        })
        .find(|&i| hit(i))
}

/// Searches `data` for `needle` starting at offset `from` (inclusive), wrapping around.
/// ASCII case folding when `case` is false.
pub fn find_bytes(
    data: &[u8],
    needle: &[u8],
    case: bool,
    from: usize,
    forward: bool,
) -> Option<usize> {
    if needle.is_empty() || needle.len() > data.len() {
        return None;
    }
    let last = data.len() - needle.len();
    let at = |i: usize| {
        let w = &data[i..i + needle.len()];
        if case {
            w == needle
        } else {
            w.eq_ignore_ascii_case(needle)
        }
    };
    let n = last + 1;
    let from = if from < n {
        from
    } else if forward {
        0
    } else {
        last
    };
    (0..n)
        .map(|k| {
            if forward {
                (from + k) % n
            } else {
                (from + n - k) % n
            }
        })
        .find(|&i| at(i))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hex_row_formats_offset_hex_and_ascii() {
        let row = format_hex_row(0x10, b"Hello, world!\x00\x01\xff");
        assert_eq!(
            row,
            "00000010  48 65 6C 6C 6F 2C 20 77  6F 72 6C 64 21 00 01 FF  |Hello, world!...|"
        );
    }

    #[test]
    fn hex_row_pads_a_short_chunk() {
        let row = format_hex_row(0, b"AB");
        assert!(row.starts_with("00000000  41 42 "));
        assert!(row.ends_with(" |AB|"));
        assert_eq!(row.len(), format_hex_row(0, &[0; 16]).len() - 14);
    }

    #[test]
    fn detects_boms() {
        assert_eq!(detect_encoding(b"\xEF\xBB\xBFabc"), (Encoding::Utf8Bom, 3));
        assert_eq!(detect_encoding(b"\xFF\xFEa\0"), (Encoding::Utf16Le, 2));
        assert_eq!(detect_encoding(b"\xFE\xFF\0a"), (Encoding::Utf16Be, 2));
        assert_eq!(detect_encoding(b"plain"), (Encoding::Utf8, 0));
    }

    #[test]
    fn decodes_utf16_and_utf8() {
        let (s, e) = decode(b"\xFF\xFEh\0i\0");
        assert_eq!((s.as_str(), e), ("hi", Encoding::Utf16Le));
        let (s, e) = decode(b"\xFE\xFF\0h\0i");
        assert_eq!((s.as_str(), e), ("hi", Encoding::Utf16Be));
        let (s, e) = decode(b"\xEF\xBB\xBFcaf\xC3\xA9");
        assert_eq!((s.as_str(), e), ("café", Encoding::Utf8Bom));
        assert_eq!(decode(b"a\xFFb").0, "a\u{FFFD}b");
        assert_eq!(decode(b"a\tb").0, "a    b");
    }

    #[test]
    fn utf16_with_odd_trailing_byte_does_not_panic() {
        assert_eq!(decode(b"\xFF\xFEh\0i").0, "h");
    }

    #[test]
    fn binary_detection() {
        assert!(is_binary(b"abc\0def"));
        assert!(!is_binary(b"just text\n"));
        assert!(!is_binary(b"\xFF\xFEh\0i\0"), "UTF-16 NULs are text");
        let mut late = vec![b'a'; SNIFF];
        late.push(0);
        assert!(!is_binary(&late), "NUL after the sniffed head");
    }

    #[test]
    fn splits_lines_like_lines() {
        let t = "a\r\nbb\n\nccc";
        let v: Vec<&str> = line_ranges(t).into_iter().map(|r| &t[r]).collect();
        assert_eq!(v, ["a", "bb", "", "ccc"]);
        assert!(line_ranges("").is_empty());
        assert_eq!(line_ranges("x\n").len(), 1);
    }

    #[test]
    fn wraps_long_lines() {
        let t = "abcdefg\nxy";
        let lines = line_ranges(t);
        let rows = wrap_rows(t, &lines, 3);
        let v: Vec<(usize, &str, bool)> = rows
            .iter()
            .map(|r| (r.line, &t[r.range.clone()], r.first))
            .collect();
        assert_eq!(
            v,
            [
                (0, "abc", true),
                (0, "def", false),
                (0, "g", false),
                (1, "xy", true)
            ]
        );
    }

    #[test]
    fn wrap_handles_multibyte_and_exact_fit() {
        let t = "ééé\nééééé";
        let lines = line_ranges(t);
        let rows = wrap_rows(t, &lines, 3);
        let v: Vec<&str> = rows.iter().map(|r| &t[r.range.clone()]).collect();
        assert_eq!(v, ["ééé", "ééé", "éé"]);
    }

    #[test]
    fn clips_huge_lines() {
        let s = "x".repeat(MAX_LINE_CHARS + 5);
        assert_eq!(clip_line(&s).chars().count(), MAX_LINE_CHARS + 2);
        assert_eq!(clip_line("short"), "short");
    }

    #[test]
    fn finds_lines_case_insensitively_and_wraps() {
        let t = "Alpha\nbeta\nALPHA beta\n";
        let lines = line_ranges(t);
        assert_eq!(find_line(t, &lines, "alpha", false, 0, true), Some(0));
        assert_eq!(find_line(t, &lines, "alpha", false, 1, true), Some(2));
        assert_eq!(find_line(t, &lines, "alpha", false, 3, true), Some(0));
        assert_eq!(find_line(t, &lines, "alpha", true, 0, true), None);
        assert_eq!(find_line(t, &lines, "beta", true, 1, false), Some(1));
        assert_eq!(find_line(t, &lines, "beta", true, 0, false), Some(2));
        assert_eq!(find_line(t, &lines, "", false, 0, true), None);
    }

    #[test]
    fn finds_bytes() {
        let d = b"xxAbcxxabc";
        assert_eq!(find_bytes(d, b"abc", true, 0, true), Some(7));
        assert_eq!(find_bytes(d, b"abc", false, 0, true), Some(2));
        assert_eq!(find_bytes(d, b"abc", false, 3, true), Some(7));
        assert_eq!(find_bytes(d, b"abc", false, 8, true), Some(2));
        assert_eq!(find_bytes(d, b"abc", false, 6, false), Some(2));
        assert_eq!(find_bytes(d, b"abc", false, 1, false), Some(7));
        assert_eq!(find_bytes(d, b"zzz", false, 0, true), None);
        assert_eq!(find_bytes(b"ab", b"abc", false, 0, true), None);
    }
}
