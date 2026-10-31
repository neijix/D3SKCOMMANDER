//! Minimal dBase III/IV/FoxPro (`.dbf`) reader for the lister: field definitions plus the
//! first records as text. Memo files are not read.

use std::fs::File;
use std::io::{self, BufReader, Read, Seek, SeekFrom};
use std::path::Path;

#[derive(Clone, PartialEq, Eq, Debug)]
pub struct DbfField {
    pub name: String,
    /// dBase type letter: `C` `N` `F` `L` `D` `M` …
    pub kind: char,
    pub length: u8,
    pub decimals: u8,
}

#[derive(Clone, PartialEq, Eq, Debug)]
pub struct DbfTable {
    pub fields: Vec<DbfField>,
    /// One row per record read, one string per field.
    pub records: Vec<Vec<String>>,
    /// Parallel to `records`: the record carries the deletion mark (`*`).
    pub deleted: Vec<bool>,
    /// Record count announced by the header (may exceed `records.len()`).
    pub total_records: u32,
}

const HEADER_LEN: usize = 32;
const FIELD_TERMINATOR: u8 = 0x0D;
const EOF_MARK: u8 = 0x1A;

fn bad(msg: &str) -> io::Error {
    io::Error::new(
        io::ErrorKind::InvalidData,
        format!("not a valid DBF file: {msg}"),
    )
}

fn u16_le(b: &[u8]) -> usize {
    usize::from(u16::from_le_bytes([b[0], b[1]]))
}

/// Bytes as Latin-1 text (the code page byte is not interpreted).
fn latin1(bytes: &[u8]) -> String {
    bytes.iter().map(|&b| char::from(b)).collect()
}

fn parse_field(d: &[u8]) -> DbfField {
    let name_bytes = d[..11].split(|&b| b == 0).next().unwrap_or_default();
    DbfField {
        name: latin1(name_bytes).trim().to_string(),
        kind: char::from(d[11]).to_ascii_uppercase(),
        length: d[16],
        decimals: d[17],
    }
}

fn format_value(kind: char, raw: &[u8]) -> String {
    let text = latin1(raw);
    let trimmed = text.trim_matches(|c: char| c == ' ' || c == '\0');
    match kind {
        'C' => text.trim_end_matches([' ', '\0']).to_string(),
        'L' => match trimmed.to_ascii_uppercase().as_str() {
            "T" | "Y" => "T".to_string(),
            "F" | "N" => "F".to_string(),
            _ => String::new(),
        },
        'D' if trimmed.len() == 8 && trimmed.bytes().all(|b| b.is_ascii_digit()) => {
            format!("{}-{}-{}", &trimmed[..4], &trimmed[4..6], &trimmed[6..])
        }
        _ => trimmed.to_string(),
    }
}

fn parse_record(rec: &[u8], fields: &[DbfField]) -> Vec<String> {
    let mut pos = 1; // byte 0 is the deletion flag
    fields
        .iter()
        .map(|f| {
            let end = pos + usize::from(f.length);
            let raw = rec.get(pos..end.min(rec.len())).unwrap_or_default();
            pos = end;
            format_value(f.kind, raw)
        })
        .collect()
}

/// Read the header and up to `max_records` records. dBase III/IV/FoxPro layout; field types
/// `C N F L D` are decoded (others shown raw); text is Latin-1. A file cut short yields the
/// records that are complete.
pub fn read_dbf(path: &Path, max_records: usize) -> io::Result<DbfTable> {
    let mut r = BufReader::new(File::open(path)?);
    let mut head = [0u8; HEADER_LEN];
    r.read_exact(&mut head)
        .map_err(|_| bad("header is truncated"))?;
    let total_records = u32::from_le_bytes([head[4], head[5], head[6], head[7]]);
    let header_len = u16_le(&head[8..10]);
    let record_len = u16_le(&head[10..12]);
    if header_len <= HEADER_LEN || record_len == 0 {
        return Err(bad("bad header or record length"));
    }
    let mut descriptors = vec![0u8; header_len - HEADER_LEN];
    r.read_exact(&mut descriptors)
        .map_err(|_| bad("field descriptors are truncated"))?;
    let fields: Vec<DbfField> = descriptors
        .chunks_exact(HEADER_LEN)
        .take_while(|d| d[0] != FIELD_TERMINATOR)
        .map(parse_field)
        .collect();
    if fields.is_empty() {
        return Err(bad("no fields"));
    }
    r.seek(SeekFrom::Start(header_len as u64))?;
    let want = usize::try_from(total_records).map_or(max_records, |t| t.min(max_records));
    let (records, deleted) = read_records(&mut r, &fields, record_len, want);
    Ok(DbfTable {
        fields,
        records,
        deleted,
        total_records,
    })
}

fn read_records(
    r: &mut impl Read,
    fields: &[DbfField],
    record_len: usize,
    want: usize,
) -> (Vec<Vec<String>>, Vec<bool>) {
    let mut records = Vec::new();
    let mut deleted = Vec::new();
    let mut buf = vec![0u8; record_len];
    while records.len() < want {
        if r.read_exact(&mut buf).is_err() || buf[0] == EOF_MARK {
            break;
        }
        deleted.push(buf[0] == b'*');
        records.push(parse_record(&buf, fields));
    }
    (records, deleted)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn descriptor(name: &str, kind: u8, len: u8, dec: u8) -> [u8; 32] {
        let mut d = [0u8; 32];
        d[..name.len()].copy_from_slice(name.as_bytes());
        d[11] = kind;
        d[16] = len;
        d[17] = dec;
        d
    }

    /// dBase III file: NAME C(10), AGE N(3), BORN D(8), OK L(1); records given as byte rows.
    fn build(records: &[(&str, &[u8; 10], &str, &str, &str)], announced: u32) -> Vec<u8> {
        let fields = [
            descriptor("NAME", b'C', 10, 0),
            descriptor("AGE", b'N', 3, 0),
            descriptor("BORN", b'D', 8, 0),
            descriptor("OK", b'L', 1, 0),
        ];
        let header_len = 32 + 32 * fields.len() + 1;
        let record_len = 1 + 10 + 3 + 8 + 1;
        let mut out = vec![0u8; 32];
        out[0] = 0x03;
        out[1..4].copy_from_slice(&[126, 10, 31]);
        out[4..8].copy_from_slice(&announced.to_le_bytes());
        out[8..10].copy_from_slice(&(header_len as u16).to_le_bytes());
        out[10..12].copy_from_slice(&(record_len as u16).to_le_bytes());
        for f in &fields {
            out.extend_from_slice(f);
        }
        out.push(0x0D);
        for (flag, name, age, born, ok) in records {
            out.extend_from_slice(flag.as_bytes());
            out.extend_from_slice(*name);
            out.extend_from_slice(age.as_bytes());
            out.extend_from_slice(born.as_bytes());
            out.extend_from_slice(ok.as_bytes());
        }
        out.push(0x1A);
        out
    }

    fn sample() -> Vec<u8> {
        build(
            &[
                (" ", b"Alice     ", " 30", "19900101", "T"),
                ("*", b"Bob       ", " 41", "19800231", "F"),
                (" ", b"Ren\xe9e     ", "  7", "        ", "?"),
            ],
            3,
        )
    }

    fn read(bytes: &[u8], max: usize) -> io::Result<DbfTable> {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("t.dbf");
        fs::write(&p, bytes).unwrap();
        read_dbf(&p, max)
    }

    #[test]
    fn parses_fields_and_records() {
        let t = read(&sample(), 100).unwrap();
        assert_eq!(t.total_records, 3);
        let names: Vec<&str> = t.fields.iter().map(|f| f.name.as_str()).collect();
        assert_eq!(names, ["NAME", "AGE", "BORN", "OK"]);
        assert_eq!(t.fields[0].kind, 'C');
        assert_eq!(t.fields[0].length, 10);
        assert_eq!(t.records[0], ["Alice", "30", "1990-01-01", "T"]);
        assert_eq!(t.records.len(), 3);
    }

    #[test]
    fn deleted_flag_is_reported() {
        let t = read(&sample(), 100).unwrap();
        assert_eq!(t.deleted, [false, true, false]);
        assert_eq!(t.records[1][0], "Bob");
    }

    #[test]
    fn latin1_and_blank_values() {
        let t = read(&sample(), 100).unwrap();
        assert_eq!(t.records[2], ["Ren\u{e9}e", "7", "", ""]);
    }

    #[test]
    fn max_records_limits_but_total_is_kept() {
        let t = read(&sample(), 2).unwrap();
        assert_eq!(t.records.len(), 2);
        assert_eq!(t.deleted.len(), 2);
        assert_eq!(t.total_records, 3);
    }

    #[test]
    fn truncated_data_returns_complete_records() {
        let mut bytes = sample();
        bytes.truncate(bytes.len() - 10); // cut inside the last record, drop the EOF mark
        let t = read(&bytes, 100).unwrap();
        assert_eq!(t.records.len(), 2);
        assert_eq!(t.total_records, 3);
    }

    #[test]
    fn rejects_garbage() {
        assert_eq!(
            read(b"short", 10).unwrap_err().kind(),
            io::ErrorKind::InvalidData
        );
        let zeros = vec![0u8; 64];
        assert_eq!(
            read(&zeros, 10).unwrap_err().kind(),
            io::ErrorKind::InvalidData
        );
    }
}
