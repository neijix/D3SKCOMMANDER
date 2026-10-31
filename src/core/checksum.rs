//! Checksums: CRC32, MD5, SHA-1 and SHA-256 of files, plus writing and verifying
//! `.sfv` / `.md5` / `.sha` / `.sha256` lists.

use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

use md5::{Digest, Md5};
use sha1::Sha1;
use sha2::Sha256;

const BUF_SIZE: usize = 1 << 20;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Algo {
    Crc32,
    Md5,
    Sha1,
    Sha256,
}

impl Algo {
    /// Conventional extension of a list file: `sfv md5 sha sha256`.
    pub fn extension(&self) -> &'static str {
        match self {
            Algo::Crc32 => "sfv",
            Algo::Md5 => "md5",
            Algo::Sha1 => "sha",
            Algo::Sha256 => "sha256",
        }
    }

    pub fn all() -> &'static [Algo] {
        &[Algo::Crc32, Algo::Md5, Algo::Sha1, Algo::Sha256]
    }

    pub fn label(&self) -> &'static str {
        match self {
            Algo::Crc32 => "CRC32",
            Algo::Md5 => "MD5",
            Algo::Sha1 => "SHA-1",
            Algo::Sha256 => "SHA-256",
        }
    }

    fn from_extension(ext: &str) -> Option<Algo> {
        match ext.to_ascii_lowercase().as_str() {
            "sfv" => Some(Algo::Crc32),
            "md5" => Some(Algo::Md5),
            "sha" | "sha1" => Some(Algo::Sha1),
            "sha256" => Some(Algo::Sha256),
            _ => None,
        }
    }

    fn from_hex_len(len: usize) -> Option<Algo> {
        match len {
            8 => Some(Algo::Crc32),
            32 => Some(Algo::Md5),
            40 => Some(Algo::Sha1),
            64 => Some(Algo::Sha256),
            _ => None,
        }
    }
}

fn cancelled() -> io::Error {
    io::Error::new(io::ErrorKind::Interrupted, "cancelled")
}

fn hex(bytes: &[u8], upper: bool) -> String {
    let mut s = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        let part = if upper {
            format!("{b:02X}")
        } else {
            format!("{b:02x}")
        };
        s.push_str(&part);
    }
    s
}

/// Stream `r` through `feed`, checking `cancel` between chunks.
fn pump(r: &mut impl Read, cancel: &AtomicBool, mut feed: impl FnMut(&[u8])) -> io::Result<()> {
    let mut buf = vec![0u8; BUF_SIZE];
    loop {
        if cancel.load(Ordering::Relaxed) {
            return Err(cancelled());
        }
        let n = r.read(&mut buf)?;
        if n == 0 {
            return Ok(());
        }
        feed(&buf[..n]);
    }
}

fn digest_reader<D: Digest>(r: &mut impl Read, cancel: &AtomicBool) -> io::Result<String> {
    let mut d = D::new();
    pump(r, cancel, |chunk| d.update(chunk))?;
    Ok(hex(d.finalize().as_slice(), false))
}

/// Hash everything `r` yields. Lowercase hex (CRC32: uppercase, 8 digits).
pub fn hash_reader(r: &mut impl Read, algo: Algo, cancel: &AtomicBool) -> io::Result<String> {
    match algo {
        Algo::Crc32 => {
            let mut h = crc32fast::Hasher::new();
            pump(r, cancel, |chunk| h.update(chunk))?;
            Ok(format!("{:08X}", h.finalize()))
        }
        Algo::Md5 => digest_reader::<Md5>(r, cancel),
        Algo::Sha1 => digest_reader::<Sha1>(r, cancel),
        Algo::Sha256 => digest_reader::<Sha256>(r, cancel),
    }
}

/// Lowercase hex digest of a file (CRC32: uppercase, 8 digits). Cancel → `Interrupted`.
pub fn hash_file(path: &Path, algo: Algo, cancel: &AtomicBool) -> io::Result<String> {
    let mut f = File::open(path)?;
    hash_reader(&mut f, algo, cancel)
}

/// `path` relative to `base`, '/'-separated; the full path when it is not under `base`.
fn display_rel(path: &Path, base: &Path) -> String {
    let rel = path.strip_prefix(base).unwrap_or(path);
    let parts: Vec<String> = rel
        .components()
        .filter_map(|c| match c {
            Component::Normal(s) => Some(s.to_string_lossy().into_owned()),
            Component::ParentDir => Some("..".to_string()),
            _ => None,
        })
        .collect();
    parts.join("/")
}

/// Write a checksum list for `files` to `out`: SFV (`<path> <CRC>`) for CRC32, else the
/// md5sum/sha*sum format (`<hex> *<path>`). Paths are relative to `base`, '/'-separated.
/// Returns the number of lines written.
pub fn write_checksum_file(
    files: &[PathBuf],
    base: &Path,
    algo: Algo,
    out: &Path,
    cancel: &AtomicBool,
) -> io::Result<usize> {
    let mut text = String::new();
    for f in files {
        let digest = hash_file(f, algo, cancel)
            .map_err(|e| io::Error::new(e.kind(), format!("{}: {e}", f.display())))?;
        let rel = display_rel(f, base);
        if algo == Algo::Crc32 {
            text.push_str(&format!("{rel} {digest}\n"));
        } else {
            text.push_str(&format!("{digest} *{rel}\n"));
        }
    }
    File::create(out)?.write_all(text.as_bytes())?;
    Ok(files.len())
}

#[derive(Clone, PartialEq, Eq, Debug)]
pub enum Verify {
    Ok,
    Mismatch,
    Missing,
    Error(String),
}

/// One parsed list line: (expected hex, path as written).
fn parse_line(line: &str, sfv: bool) -> Option<(String, String)> {
    let line = line.trim_end_matches(['\r', '\n']);
    if line.trim().is_empty() || line.starts_with(';') || line.starts_with('#') {
        return None;
    }
    if sfv {
        let (path, crc) = line.trim_end().rsplit_once(' ')?;
        return Some((crc.trim().to_string(), path.trim().to_string()));
    }
    let (digest, rest) = line.trim_start().split_once(' ')?;
    let path = rest.strip_prefix(['*', ' ']).unwrap_or(rest);
    Some((digest.to_string(), path.to_string()))
}

fn resolve(base: &Path, rel: &str) -> PathBuf {
    let mut p = base.to_path_buf();
    for part in rel.split(['/', '\\']).filter(|s| !s.is_empty()) {
        p.push(part);
    }
    p
}

fn verify_entry(
    base: &Path,
    expected: &str,
    rel: &str,
    algo: Option<Algo>,
    cancel: &AtomicBool,
) -> Verify {
    let Some(algo) = algo.or_else(|| Algo::from_hex_len(expected.len())) else {
        return Verify::Error("cannot tell the checksum algorithm".to_string());
    };
    let path = resolve(base, rel);
    if !path.is_file() {
        return Verify::Missing;
    }
    match hash_file(&path, algo, cancel) {
        Ok(got) if got.eq_ignore_ascii_case(expected) => Verify::Ok,
        Ok(_) => Verify::Mismatch,
        Err(e) => Verify::Error(e.to_string()),
    }
}

/// Check every file of a `.sfv/.md5/.sha/.sha256` list (paths relative to the list's folder).
/// The algorithm comes from the list's extension, else from each hex length.
/// Cancel → `Interrupted`.
pub fn verify_checksum_file(list: &Path, cancel: &AtomicBool) -> io::Result<Vec<(String, Verify)>> {
    let bytes = fs::read(list)?;
    let text = String::from_utf8_lossy(&bytes);
    let algo = list
        .extension()
        .and_then(|e| e.to_str())
        .and_then(Algo::from_extension);
    let base = list.parent().unwrap_or(Path::new("."));
    let mut out = Vec::new();
    for line in text.lines() {
        let Some((expected, rel)) = parse_line(line, algo == Some(Algo::Crc32)) else {
            continue;
        };
        let v = verify_entry(base, &expected, &rel, algo, cancel);
        if cancel.load(Ordering::Relaxed) {
            return Err(cancelled());
        }
        out.push((rel, v));
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn no_cancel() -> AtomicBool {
        AtomicBool::new(false)
    }

    fn hash_bytes(data: &[u8], algo: Algo) -> String {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("f");
        fs::write(&p, data).unwrap();
        hash_file(&p, algo, &no_cancel()).unwrap()
    }

    #[test]
    fn known_vectors_for_abc() {
        assert_eq!(hash_bytes(b"abc", Algo::Crc32), "352441C2");
        assert_eq!(
            hash_bytes(b"abc", Algo::Md5),
            "900150983cd24fb0d6963f7d28e17f72"
        );
        assert_eq!(
            hash_bytes(b"abc", Algo::Sha1),
            "a9993e364706816aba3e25717850c26c9cd0d89d"
        );
        assert_eq!(
            hash_bytes(b"abc", Algo::Sha256),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
    }

    #[test]
    fn empty_file_vectors() {
        assert_eq!(hash_bytes(b"", Algo::Crc32), "00000000");
        assert_eq!(
            hash_bytes(b"", Algo::Md5),
            "d41d8cd98f00b204e9800998ecf8427e"
        );
    }

    #[test]
    fn cancel_interrupts() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("f");
        fs::write(&p, b"abc").unwrap();
        let cancel = AtomicBool::new(true);
        let err = hash_file(&p, Algo::Md5, &cancel).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Interrupted);
    }

    #[test]
    fn algo_metadata() {
        assert_eq!(Algo::all().len(), 4);
        assert_eq!(Algo::Crc32.extension(), "sfv");
        assert_eq!(Algo::Sha1.extension(), "sha");
        assert_eq!(Algo::Sha256.label(), "SHA-256");
    }

    fn sample_tree(dir: &Path) -> Vec<PathBuf> {
        fs::create_dir(dir.join("sub")).unwrap();
        fs::write(dir.join("a.txt"), b"abc").unwrap();
        fs::write(dir.join("sub").join("b.txt"), b"hello").unwrap();
        vec![dir.join("a.txt"), dir.join("sub").join("b.txt")]
    }

    #[test]
    fn write_formats() {
        let dir = tempfile::tempdir().unwrap();
        let files = sample_tree(dir.path());
        let md5 = dir.path().join("x.md5");
        let n = write_checksum_file(&files, dir.path(), Algo::Md5, &md5, &no_cancel()).unwrap();
        assert_eq!(n, 2);
        let text = fs::read_to_string(&md5).unwrap();
        assert!(text.contains("900150983cd24fb0d6963f7d28e17f72 *a.txt\n"));
        assert!(text.contains(" *sub/b.txt\n"));
        let sfv = dir.path().join("x.sfv");
        write_checksum_file(&files, dir.path(), Algo::Crc32, &sfv, &no_cancel()).unwrap();
        assert!(
            fs::read_to_string(&sfv)
                .unwrap()
                .starts_with("a.txt 352441C2\n")
        );
    }

    #[test]
    fn verify_round_trip_all_algos() {
        for algo in Algo::all() {
            let dir = tempfile::tempdir().unwrap();
            let files = sample_tree(dir.path());
            let list = dir.path().join(format!("list.{}", algo.extension()));
            write_checksum_file(&files, dir.path(), *algo, &list, &no_cancel()).unwrap();
            let res = verify_checksum_file(&list, &no_cancel()).unwrap();
            assert_eq!(res.len(), 2);
            assert!(
                res.iter().all(|(_, v)| *v == Verify::Ok),
                "{algo:?}: {res:?}"
            );
        }
    }

    #[test]
    fn verify_detects_mismatch_and_missing() {
        let dir = tempfile::tempdir().unwrap();
        let files = sample_tree(dir.path());
        let list = dir.path().join("list.sha256");
        write_checksum_file(&files, dir.path(), Algo::Sha256, &list, &no_cancel()).unwrap();
        fs::write(&files[0], b"changed").unwrap();
        fs::remove_file(&files[1]).unwrap();
        let res = verify_checksum_file(&list, &no_cancel()).unwrap();
        assert_eq!(res[0], ("a.txt".to_string(), Verify::Mismatch));
        assert_eq!(res[1], ("sub/b.txt".to_string(), Verify::Missing));
    }

    #[test]
    fn verify_guesses_algo_from_hex_length() {
        let dir = tempfile::tempdir().unwrap();
        fs::write(dir.path().join("a.txt"), b"abc").unwrap();
        let list = dir.path().join("sums.txt");
        fs::write(
            &list,
            "# comment\n900150983cd24fb0d6963f7d28e17f72  a.txt\r\nnothex *a.txt\n",
        )
        .unwrap();
        let res = verify_checksum_file(&list, &no_cancel()).unwrap();
        assert_eq!(res[0].1, Verify::Ok);
        assert!(matches!(res[1].1, Verify::Error(_)));
    }

    #[test]
    fn verify_sfv_skips_comments_and_spaces_in_names() {
        let dir = tempfile::tempdir().unwrap();
        fs::write(dir.path().join("my file.txt"), b"abc").unwrap();
        let list = dir.path().join("l.sfv");
        fs::write(&list, "; made by test\nmy file.txt 352441c2\n").unwrap();
        let res = verify_checksum_file(&list, &no_cancel()).unwrap();
        assert_eq!(res, vec![("my file.txt".to_string(), Verify::Ok)]);
    }
}
