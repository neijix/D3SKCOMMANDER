//! File comparison: a side-by-side text diff and a byte-wise binary compare.

use std::fs::File;
use std::io::{self, Read};
use std::path::Path;

use similar::{Algorithm, DiffOp, capture_diff_slices};

/// How a row of the side-by-side view relates its two sides.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum RowKind {
    Equal,
    Changed,
    LeftOnly,
    RightOnly,
}

/// One row of the side-by-side view; line numbers are 1-based.
#[derive(Clone, PartialEq, Eq, Debug)]
pub struct DiffRow {
    pub kind: RowKind,
    pub left_no: Option<usize>,
    pub left: Option<String>,
    pub right_no: Option<usize>,
    pub right: Option<String>,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct DiffOptions {
    pub ignore_case: bool,
    pub ignore_whitespace: bool,
}

/// Files with a NUL in their first `BINARY_SNIFF` bytes are binary.
const BINARY_SNIFF: usize = 8 * 1024;
/// Files larger than this are compared byte-wise, never as text.
const MAX_TEXT_SIZE: u64 = 32 * 1024 * 1024;

fn normalize(line: &str, opts: &DiffOptions) -> String {
    let mut s: String = if opts.ignore_whitespace {
        line.chars().filter(|c| !c.is_whitespace()).collect()
    } else {
        line.to_string()
    };
    if opts.ignore_case {
        s = s.to_lowercase();
    }
    s
}

fn row(kind: RowKind, l: Option<(usize, &str)>, r: Option<(usize, &str)>) -> DiffRow {
    DiffRow {
        kind,
        left_no: l.map(|(n, _)| n + 1),
        left: l.map(|(_, t)| t.to_string()),
        right_no: r.map(|(n, _)| n + 1),
        right: r.map(|(_, t)| t.to_string()),
    }
}

/// Side-by-side diff of two texts. A deleted run followed by an inserted run pairs up line by
/// line as `Changed`; the surplus lines are `LeftOnly` / `RightOnly`.
pub fn diff_text(a: &str, b: &str, opts: &DiffOptions) -> Vec<DiffRow> {
    let left: Vec<&str> = a.lines().collect();
    let right: Vec<&str> = b.lines().collect();
    let nl: Vec<String> = left.iter().map(|l| normalize(l, opts)).collect();
    let nr: Vec<String> = right.iter().map(|l| normalize(l, opts)).collect();
    let mut rows = Vec::new();
    for op in capture_diff_slices(Algorithm::Myers, &nl, &nr) {
        match op {
            DiffOp::Equal {
                old_index,
                new_index,
                len,
            } => {
                for k in 0..len {
                    let (i, j) = (old_index + k, new_index + k);
                    rows.push(row(RowKind::Equal, Some((i, left[i])), Some((j, right[j]))));
                }
            }
            DiffOp::Delete {
                old_index, old_len, ..
            } => {
                for (i, text) in left.iter().enumerate().skip(old_index).take(old_len) {
                    rows.push(row(RowKind::LeftOnly, Some((i, text)), None));
                }
            }
            DiffOp::Insert {
                new_index, new_len, ..
            } => {
                for (j, text) in right.iter().enumerate().skip(new_index).take(new_len) {
                    rows.push(row(RowKind::RightOnly, None, Some((j, text))));
                }
            }
            DiffOp::Replace {
                old_index,
                old_len,
                new_index,
                new_len,
            } => {
                for k in 0..old_len.max(new_len) {
                    let l = (k < old_len).then(|| (old_index + k, left[old_index + k]));
                    let r = (k < new_len).then(|| (new_index + k, right[new_index + k]));
                    let kind = match (l, r) {
                        (Some(_), Some(_)) => RowKind::Changed,
                        (Some(_), None) => RowKind::LeftOnly,
                        _ => RowKind::RightOnly,
                    };
                    rows.push(row(kind, l, r));
                }
            }
        }
    }
    rows
}

#[derive(Clone, PartialEq, Eq, Debug)]
pub enum FileDiff {
    Text(Vec<DiffRow>),
    /// `first_difference` is the offset of the first differing byte (the shorter length when
    /// one file is a prefix of the other); `None` when identical.
    Binary {
        identical: bool,
        first_difference: Option<u64>,
    },
}

fn read_full(f: &mut impl Read, buf: &mut [u8]) -> io::Result<usize> {
    let mut n = 0;
    while n < buf.len() {
        match f.read(&mut buf[n..]) {
            Ok(0) => break,
            Ok(k) => n += k,
            Err(e) if e.kind() == io::ErrorKind::Interrupted => {}
            Err(e) => return Err(e),
        }
    }
    Ok(n)
}

fn has_nul_head(path: &Path) -> io::Result<bool> {
    let mut buf = vec![0u8; BINARY_SNIFF];
    let n = read_full(&mut File::open(path)?, &mut buf)?;
    Ok(buf[..n].contains(&0))
}

/// Offset of the first differing byte, `None` when the files are identical.
fn first_difference(a: &Path, b: &Path) -> io::Result<Option<u64>> {
    const CHUNK: usize = 64 * 1024;
    let (mut fa, mut fb) = (File::open(a)?, File::open(b)?);
    let (mut ba, mut bb) = (vec![0u8; CHUNK], vec![0u8; CHUNK]);
    let mut offset = 0u64;
    loop {
        let na = read_full(&mut fa, &mut ba)?;
        let nb = read_full(&mut fb, &mut bb)?;
        let common = na.min(nb);
        if let Some(i) = (0..common).find(|&i| ba[i] != bb[i]) {
            return Ok(Some(offset + i as u64));
        }
        if na != nb {
            return Ok(Some(offset + common as u64));
        }
        if na == 0 {
            return Ok(None);
        }
        offset += na as u64;
    }
}

/// Byte-wise equality of two files (sizes first, then a streamed compare).
pub fn files_identical(a: &Path, b: &Path) -> io::Result<bool> {
    if std::fs::metadata(a)?.len() != std::fs::metadata(b)?.len() {
        return Ok(false);
    }
    Ok(first_difference(a, b)?.is_none())
}

/// Compare two files: as text, unless either holds a NUL in its first 8 KiB or exceeds 32 MiB.
pub fn diff_files(a: &Path, b: &Path, opts: &DiffOptions) -> io::Result<FileDiff> {
    let too_big =
        std::fs::metadata(a)?.len() > MAX_TEXT_SIZE || std::fs::metadata(b)?.len() > MAX_TEXT_SIZE;
    if too_big || has_nul_head(a)? || has_nul_head(b)? {
        let first = first_difference(a, b)?;
        return Ok(FileDiff::Binary {
            identical: first.is_none(),
            first_difference: first,
        });
    }
    let (ta, tb) = (std::fs::read(a)?, std::fs::read(b)?);
    Ok(FileDiff::Text(diff_text(
        &String::from_utf8_lossy(&ta),
        &String::from_utf8_lossy(&tb),
        opts,
    )))
}

/// True when row `i` begins a difference block (a run of non-`Equal` rows).
fn is_block_start(rows: &[DiffRow], i: usize) -> bool {
    rows[i].kind != RowKind::Equal && (i == 0 || rows[i - 1].kind == RowKind::Equal)
}

/// First row after `from` that starts a difference block.
pub fn next_difference(rows: &[DiffRow], from: usize) -> Option<usize> {
    (from.saturating_add(1)..rows.len()).find(|&i| is_block_start(rows, i))
}

/// Start of the nearest difference block before `from`; when `from` is inside a block, that is
/// the block before the current one.
pub fn prev_difference(rows: &[DiffRow], from: usize) -> Option<usize> {
    let mut i = from.min(rows.len());
    // Leave the block `from` sits in, so repeated calls walk backwards block by block.
    while i > 0
        && i < rows.len()
        && rows[i].kind != RowKind::Equal
        && rows[i - 1].kind != RowKind::Equal
    {
        i -= 1;
    }
    (0..i).rev().find(|&k| is_block_start(rows, k))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn opts() -> DiffOptions {
        DiffOptions::default()
    }

    fn kinds(rows: &[DiffRow]) -> Vec<RowKind> {
        rows.iter().map(|r| r.kind).collect()
    }

    #[test]
    fn equal_texts() {
        let rows = diff_text("a\nb\n", "a\nb\n", &opts());
        assert_eq!(kinds(&rows), [RowKind::Equal, RowKind::Equal]);
        assert_eq!(rows[1].left_no, Some(2));
        assert_eq!(rows[1].right_no, Some(2));
        assert_eq!(rows[1].left.as_deref(), Some("b"));
        assert!(diff_text("", "", &opts()).is_empty());
    }

    #[test]
    fn insert_and_delete() {
        let rows = diff_text("a\nc\n", "a\nb\nc\n", &opts());
        assert_eq!(
            kinds(&rows),
            [RowKind::Equal, RowKind::RightOnly, RowKind::Equal]
        );
        assert_eq!(rows[1].left_no, None);
        assert_eq!(rows[1].right_no, Some(2));
        assert_eq!(rows[2].left_no, Some(2));
        assert_eq!(rows[2].right_no, Some(3));

        let rows = diff_text("a\nb\nc\n", "a\nc\n", &opts());
        assert_eq!(
            kinds(&rows),
            [RowKind::Equal, RowKind::LeftOnly, RowKind::Equal]
        );
        assert_eq!(rows[1].left.as_deref(), Some("b"));
        assert_eq!(rows[1].right, None);
        assert_eq!(rows[2].right_no, Some(2));
    }

    #[test]
    fn change_pairs_lines_and_keeps_surplus() {
        let rows = diff_text("x\nold1\nold2\nold3\ny\n", "x\nnew1\nnew2\ny\n", &opts());
        assert_eq!(
            kinds(&rows),
            [
                RowKind::Equal,
                RowKind::Changed,
                RowKind::Changed,
                RowKind::LeftOnly,
                RowKind::Equal
            ]
        );
        assert_eq!(rows[1].left.as_deref(), Some("old1"));
        assert_eq!(rows[1].right.as_deref(), Some("new1"));
        assert_eq!((rows[2].left_no, rows[2].right_no), (Some(3), Some(3)));
        assert_eq!((rows[3].left_no, rows[3].right_no), (Some(4), None));
        assert_eq!((rows[4].left_no, rows[4].right_no), (Some(5), Some(4)));

        let rows = diff_text("a\n", "b\nc\n", &opts());
        assert_eq!(kinds(&rows), [RowKind::Changed, RowKind::RightOnly]);
    }

    #[test]
    fn ignore_case_and_whitespace() {
        let strict = diff_text("Hello  World\n", "hello world\n", &opts());
        assert_eq!(kinds(&strict), [RowKind::Changed]);

        let case = DiffOptions {
            ignore_case: true,
            ignore_whitespace: false,
        };
        let rows = diff_text("Hello World\n", "hello world\n", &case);
        assert_eq!(kinds(&rows), [RowKind::Equal]);
        assert_eq!(rows[0].left.as_deref(), Some("Hello World"));
        assert_eq!(rows[0].right.as_deref(), Some("hello world"));

        let ws = DiffOptions {
            ignore_case: false,
            ignore_whitespace: true,
        };
        assert_eq!(kinds(&diff_text("a \t b\n", "ab\n", &ws)), [RowKind::Equal]);
        assert_eq!(kinds(&diff_text("a b\n", "AB\n", &ws)), [RowKind::Changed]);
    }

    #[test]
    fn crlf_lines_compare_equal() {
        assert_eq!(
            kinds(&diff_text("a\r\nb\r\n", "a\nb\n", &opts())),
            [RowKind::Equal; 2]
        );
    }

    #[test]
    fn binary_detection_and_first_difference() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a.bin"), dir.path().join("b.bin"));
        std::fs::write(&a, [1u8, 2, 0, 4, 5]).unwrap();
        std::fs::write(&b, [1u8, 2, 0, 9, 5]).unwrap();
        assert_eq!(
            diff_files(&a, &b, &opts()).unwrap(),
            FileDiff::Binary {
                identical: false,
                first_difference: Some(3)
            }
        );
        std::fs::write(&b, [1u8, 2, 0, 4, 5]).unwrap();
        assert_eq!(
            diff_files(&a, &b, &opts()).unwrap(),
            FileDiff::Binary {
                identical: true,
                first_difference: None
            }
        );
        // a prefix differs at the shorter length
        std::fs::write(&b, [1u8, 2, 0]).unwrap();
        assert_eq!(
            diff_files(&a, &b, &opts()).unwrap(),
            FileDiff::Binary {
                identical: false,
                first_difference: Some(3)
            }
        );
        // NUL in only one of the files is enough
        std::fs::write(&b, "text").unwrap();
        assert!(matches!(
            diff_files(&a, &b, &opts()).unwrap(),
            FileDiff::Binary { .. }
        ));
    }

    #[test]
    fn first_difference_crosses_chunks() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a"), dir.path().join("b"));
        let mut data = vec![7u8; 200_000];
        std::fs::write(&a, &data).unwrap();
        data[150_000] = 8;
        std::fs::write(&b, &data).unwrap();
        assert!(!files_identical(&a, &b).unwrap());
        assert_eq!(first_difference(&a, &b).unwrap(), Some(150_000));
    }

    #[test]
    fn text_files_diff_as_text() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a.txt"), dir.path().join("b.txt"));
        std::fs::write(&a, "one\ntwo\n").unwrap();
        std::fs::write(&b, "one\n2\n").unwrap();
        match diff_files(&a, &b, &opts()).unwrap() {
            FileDiff::Text(rows) => assert_eq!(kinds(&rows), [RowKind::Equal, RowKind::Changed]),
            other => panic!("expected text, got {other:?}"),
        }
        assert!(files_identical(&a, &a).unwrap());
        assert!(!files_identical(&a, &b).unwrap());
        assert!(diff_files(&a, &dir.path().join("missing"), &opts()).is_err());
    }

    #[test]
    fn next_and_prev_difference() {
        // two difference blocks: lines b/c changed, then the tail
        let rows = diff_text("a\nb\nc\nd\ne\nf\n", "a\nB\nC\nd\nf\nf2\n", &opts());
        let starts: Vec<usize> = (0..rows.len())
            .filter(|&i| is_block_start(&rows, i))
            .collect();
        assert!(starts.len() >= 2, "{rows:?}");
        let first = starts[0];
        assert_eq!(next_difference(&rows, 0), Some(first));
        assert_eq!(next_difference(&rows, first), Some(starts[1]));
        assert_eq!(next_difference(&rows, *starts.last().unwrap()), None);
        assert_eq!(prev_difference(&rows, first), None);
        assert_eq!(prev_difference(&rows, starts[1]), Some(first));
        // from the middle of a block, go to the previous block, not the block's own start
        assert_eq!(prev_difference(&rows, first + 1), None);
        assert_eq!(
            prev_difference(&rows, rows.len() + 5),
            starts.last().copied()
        );
        assert_eq!(next_difference(&[], 0), None);
        assert_eq!(prev_difference(&[], 0), None);
        assert_eq!(next_difference(&rows, usize::MAX), None);
    }

    #[test]
    fn no_difference_in_equal_rows() {
        let rows = diff_text("a\nb\n", "a\nb\n", &opts());
        assert_eq!(next_difference(&rows, 0), None);
        assert_eq!(prev_difference(&rows, 1), None);
    }
}
