//! File splitter and combiner (Total Commander style): `name.001`, `name.002`, … plus a
//! `name.crc` file used to check the recombined file.

use std::fs::{self, File};
use std::io::{self, BufWriter, Read, Write};
use std::path::{Path, PathBuf};

const BUF_SIZE: usize = 1 << 20;

fn cancelled() -> io::Error {
    io::Error::new(io::ErrorKind::Interrupted, "cancelled")
}

fn invalid(msg: impl Into<String>) -> io::Error {
    io::Error::new(io::ErrorKind::InvalidInput, msg.into())
}

fn part_path(dir: &Path, name: &str, n: usize) -> PathBuf {
    dir.join(format!("{name}.{n:03}"))
}

/// Copy up to `limit` bytes from `src` to `dst`, feeding the CRC and reporting progress.
/// Returns the bytes copied, or `Interrupted` when `progress` returned `false`.
fn copy_part(
    src: &mut impl Read,
    dst: &mut impl Write,
    limit: u64,
    crc: &mut crc32fast::Hasher,
    done: &mut u64,
    total: u64,
    progress: &mut dyn FnMut(u64, u64) -> bool,
) -> io::Result<u64> {
    let mut buf = vec![0u8; BUF_SIZE];
    let mut written = 0u64;
    while written < limit {
        let want = usize::try_from(limit - written).map_or(BUF_SIZE, |w| w.min(BUF_SIZE));
        let n = src.read(&mut buf[..want])?;
        if n == 0 {
            break;
        }
        dst.write_all(&buf[..n])?;
        crc.update(&buf[..n]);
        written += n as u64;
        *done += n as u64;
        if !progress(*done, total) {
            return Err(cancelled());
        }
    }
    Ok(written)
}

fn remove_all(paths: &[PathBuf]) {
    for p in paths {
        let _ = fs::remove_file(p);
    }
}

fn write_parts(
    src: &Path,
    dest_dir: &Path,
    name: &str,
    part_size: u64,
    parts: &mut Vec<PathBuf>,
    progress: &mut dyn FnMut(u64, u64) -> bool,
) -> io::Result<(u64, u32)> {
    let mut input = File::open(src)?;
    let total = input.metadata()?.len();
    let mut crc = crc32fast::Hasher::new();
    let mut done = 0u64;
    let mut n = 0usize;
    loop {
        n += 1;
        let path = part_path(dest_dir, name, n);
        parts.push(path.clone());
        let mut out = BufWriter::new(File::create(&path)?);
        let written = copy_part(
            &mut input, &mut out, part_size, &mut crc, &mut done, total, progress,
        )?;
        out.flush()?;
        // the last part is the one that came up short (or the file is empty)
        if written < part_size {
            break;
        }
        if done >= total {
            break;
        }
    }
    Ok((total, crc.finalize()))
}

/// Cut `src` into parts of `part_size` bytes in `dest_dir` (`name.001`, `name.002`, …) and
/// write `name.crc` (`filename=…\nsize=…\ncrc32=<8 hex upper>\n`). Returns the part paths.
/// `progress(done, total)` returning `false` cancels (`Interrupted`, parts removed).
pub fn split_file(
    src: &Path,
    dest_dir: &Path,
    part_size: u64,
    progress: &mut dyn FnMut(u64, u64) -> bool,
) -> io::Result<Vec<PathBuf>> {
    if part_size == 0 {
        return Err(invalid("part size must be greater than 0"));
    }
    let name = src
        .file_name()
        .ok_or_else(|| invalid("source has no file name"))?
        .to_string_lossy()
        .into_owned();
    fs::create_dir_all(dest_dir)?;
    let mut parts = Vec::new();
    let (size, crc) = match write_parts(src, dest_dir, &name, part_size, &mut parts, progress) {
        Ok(v) => v,
        Err(e) => {
            remove_all(&parts);
            return Err(e);
        }
    };
    let crc_path = dest_dir.join(format!("{name}.crc"));
    let text = format!("filename={name}\nsize={size}\ncrc32={crc:08X}\n");
    if let Err(e) = fs::write(&crc_path, text) {
        remove_all(&parts);
        return Err(e);
    }
    Ok(parts)
}

#[derive(Debug)]
pub struct CombineReport {
    pub output: PathBuf,
    pub parts: usize,
    /// `None` when there is no readable `.crc` file next to the parts.
    pub crc_ok: Option<bool>,
}

struct CrcInfo {
    size: Option<u64>,
    crc: Option<u32>,
}

fn read_crc_file(path: &Path) -> Option<CrcInfo> {
    let text = fs::read_to_string(path).ok()?;
    let mut info = CrcInfo {
        size: None,
        crc: None,
    };
    for line in text.lines() {
        let Some((k, v)) = line.split_once('=') else {
            continue;
        };
        match k.trim().to_ascii_lowercase().as_str() {
            "size" => info.size = v.trim().parse().ok(),
            "crc32" => info.crc = u32::from_str_radix(v.trim(), 16).ok(),
            _ => {}
        }
    }
    info.crc.map(|_| info)
}

/// Base name from `name.001` / `name.crc` (any part number, any case of `crc`).
fn base_name(any_part: &Path) -> io::Result<String> {
    let file = any_part
        .file_name()
        .ok_or_else(|| invalid("not a file name"))?
        .to_string_lossy()
        .into_owned();
    let (base, ext) = file
        .rsplit_once('.')
        .ok_or_else(|| invalid(format!("{file}: not a split part (.001) or .crc file")))?;
    let numeric = !ext.is_empty() && ext.bytes().all(|b| b.is_ascii_digit());
    if base.is_empty() || !(numeric || ext.eq_ignore_ascii_case("crc")) {
        return Err(invalid(format!(
            "{file}: not a split part (.001) or .crc file"
        )));
    }
    Ok(base.to_string())
}

fn append_parts(
    dir: &Path,
    base: &str,
    out: &mut impl Write,
    progress: &mut dyn FnMut(u64, u64) -> bool,
) -> io::Result<(usize, u64, u32)> {
    let mut paths = Vec::new();
    while part_path(dir, base, paths.len() + 1).is_file() {
        paths.push(part_path(dir, base, paths.len() + 1));
    }
    if paths.is_empty() {
        return Err(io::Error::new(
            io::ErrorKind::NotFound,
            format!("{base}.001 not found in {}", dir.display()),
        ));
    }
    let mut total = 0u64;
    for p in &paths {
        total += fs::metadata(p)?.len();
    }
    let (mut crc, mut done) = (crc32fast::Hasher::new(), 0u64);
    for p in &paths {
        let mut input = File::open(p)?;
        copy_part(
            &mut input,
            out,
            u64::MAX,
            &mut crc,
            &mut done,
            total,
            progress,
        )?;
    }
    Ok((paths.len(), done, crc.finalize()))
}

/// Join `name.001`, `name.002`, … (found next to `any_part`, which may also be `name.crc`)
/// into `dest_dir/name`. When `name.crc` exists the result is checked against it.
/// `progress(done, total)` returning `false` cancels (`Interrupted`, output removed).
pub fn combine_files(
    any_part: &Path,
    dest_dir: &Path,
    progress: &mut dyn FnMut(u64, u64) -> bool,
) -> io::Result<CombineReport> {
    let base = base_name(any_part)?;
    let src_dir = match any_part.parent() {
        Some(p) if !p.as_os_str().is_empty() => p,
        _ => Path::new("."),
    };
    fs::create_dir_all(dest_dir)?;
    let output = dest_dir.join(&base);
    if part_path(src_dir, &base, 1) == output {
        return Err(invalid("output would overwrite a part"));
    }
    let mut out = BufWriter::new(File::create(&output)?);
    let result = append_parts(src_dir, &base, &mut out, progress);
    let flushed = out.flush();
    let (parts, size, crc) = match result.and_then(|r| flushed.map(|()| r)) {
        Ok(r) => r,
        Err(e) => {
            let _ = fs::remove_file(&output);
            return Err(e);
        }
    };
    let crc_ok = read_crc_file(&src_dir.join(format!("{base}.crc")))
        .map(|info| info.crc == Some(crc) && info.size.is_none_or(|s| s == size));
    Ok(CombineReport {
        output,
        parts,
        crc_ok,
    })
}

/// Parse a size: `1.44M`, `700MB`, `4G`, `100k`, `512` (binary units: k = 1024).
pub fn parse_size(s: &str) -> Result<u64, String> {
    let t = s.trim();
    let split = t
        .find(|c: char| !(c.is_ascii_digit() || c == '.' || c == ','))
        .unwrap_or(t.len());
    let (num, unit) = t.split_at(split);
    let mult: f64 = match unit.trim().to_ascii_lowercase().as_str() {
        "" | "b" => 1.0,
        "k" | "kb" | "kib" => 1024.0,
        "m" | "mb" | "mib" => 1024.0 * 1024.0,
        "g" | "gb" | "gib" => 1024.0 * 1024.0 * 1024.0,
        "t" | "tb" | "tib" => 1024.0 * 1024.0 * 1024.0 * 1024.0,
        _ => return Err(format!("invalid size: {s:?}")),
    };
    let value: f64 = num
        .replace(',', ".")
        .parse()
        .map_err(|_| format!("invalid size: {s:?}"))?;
    let bytes = (value * mult).round();
    if !bytes.is_finite() || bytes >= u64::MAX as f64 {
        return Err(format!("size too large: {s:?}"));
    }
    Ok(bytes as u64)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn yes(_: u64, _: u64) -> bool {
        true
    }

    fn sample(len: usize) -> Vec<u8> {
        (0..len).map(|i| (i * 7 % 251) as u8).collect()
    }

    fn setup(len: usize) -> (tempfile::TempDir, PathBuf, Vec<u8>) {
        let dir = tempfile::tempdir().unwrap();
        let data = sample(len);
        let src = dir.path().join("data.bin");
        fs::write(&src, &data).unwrap();
        (dir, src, data)
    }

    #[test]
    fn split_makes_parts_and_crc_file() {
        let (dir, src, data) = setup(2500);
        let out = dir.path().join("parts");
        let parts = split_file(&src, &out, 1000, &mut yes).unwrap();
        assert_eq!(parts.len(), 3);
        assert_eq!(parts[0], out.join("data.bin.001"));
        assert_eq!(fs::metadata(&parts[0]).unwrap().len(), 1000);
        assert_eq!(fs::metadata(&parts[2]).unwrap().len(), 500);
        let crc = fs::read_to_string(out.join("data.bin.crc")).unwrap();
        let expected = format!(
            "filename=data.bin\nsize=2500\ncrc32={:08X}\n",
            crc32fast::hash(&data)
        );
        assert_eq!(crc, expected);
    }

    #[test]
    fn exact_multiple_has_no_empty_trailing_part() {
        let (dir, src, _) = setup(2000);
        let parts = split_file(&src, &dir.path().join("p"), 1000, &mut yes).unwrap();
        assert_eq!(parts.len(), 2);
    }

    #[test]
    fn round_trip_with_crc_ok() {
        let (dir, src, data) = setup(5000);
        let parts_dir = dir.path().join("parts");
        let parts = split_file(&src, &parts_dir, 1024, &mut yes).unwrap();
        assert_eq!(parts.len(), 5);
        let joined = dir.path().join("joined");
        let report = combine_files(&parts[2], &joined, &mut yes).unwrap();
        assert_eq!(report.parts, 5);
        assert_eq!(report.crc_ok, Some(true));
        assert_eq!(fs::read(&report.output).unwrap(), data);
        // starting from the .crc file works too
        let again = combine_files(
            &parts_dir.join("data.bin.crc"),
            &dir.path().join("j2"),
            &mut yes,
        )
        .unwrap();
        assert_eq!(again.crc_ok, Some(true));
    }

    #[test]
    fn corrupted_part_is_detected() {
        let (dir, src, _) = setup(3000);
        let parts_dir = dir.path().join("parts");
        let parts = split_file(&src, &parts_dir, 1000, &mut yes).unwrap();
        let mut bytes = fs::read(&parts[1]).unwrap();
        bytes[10] ^= 0xFF;
        fs::write(&parts[1], bytes).unwrap();
        let report = combine_files(&parts[0], &dir.path().join("out"), &mut yes).unwrap();
        assert_eq!(report.crc_ok, Some(false));
    }

    #[test]
    fn missing_crc_file_gives_none() {
        let (dir, src, _) = setup(1500);
        let parts_dir = dir.path().join("parts");
        let parts = split_file(&src, &parts_dir, 1000, &mut yes).unwrap();
        fs::remove_file(parts_dir.join("data.bin.crc")).unwrap();
        let report = combine_files(&parts[0], &dir.path().join("out"), &mut yes).unwrap();
        assert_eq!(report.crc_ok, None);
    }

    #[test]
    fn empty_file_round_trip() {
        let (dir, src, _) = setup(0);
        let parts = split_file(&src, &dir.path().join("p"), 100, &mut yes).unwrap();
        assert_eq!(parts.len(), 1);
        let report = combine_files(&parts[0], &dir.path().join("o"), &mut yes).unwrap();
        assert_eq!(report.crc_ok, Some(true));
        assert_eq!(fs::metadata(&report.output).unwrap().len(), 0);
    }

    #[test]
    fn cancel_removes_parts() {
        let (dir, src, _) = setup(5000);
        let out = dir.path().join("p");
        let err = split_file(&src, &out, 1000, &mut |done, _| done < 2000).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::Interrupted);
        assert!(!out.join("data.bin.001").exists());
    }

    #[test]
    fn bad_inputs() {
        let (dir, src, _) = setup(10);
        assert!(split_file(&src, dir.path(), 0, &mut yes).is_err());
        let err = combine_files(&src, dir.path(), &mut yes).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::InvalidInput);
        let err = combine_files(
            &dir.path().join("nope.001"),
            &dir.path().join("o"),
            &mut yes,
        )
        .unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::NotFound);
    }

    #[test]
    fn parse_size_forms() {
        assert_eq!(parse_size("512"), Ok(512));
        assert_eq!(parse_size("100k"), Ok(102_400));
        assert_eq!(parse_size("700MB"), Ok(700 * 1024 * 1024));
        assert_eq!(parse_size("4G"), Ok(4 * 1024 * 1024 * 1024));
        assert_eq!(parse_size("1.44M"), Ok(1_509_949));
        assert_eq!(parse_size(" 2 kb "), Ok(2048));
        assert_eq!(parse_size("1,5k"), Ok(1536));
    }

    #[test]
    fn parse_size_rejects_garbage() {
        for bad in ["", "k", "12x", "-5", "1.2.3", "99999999999999999999T"] {
            assert!(parse_size(bad).is_err(), "{bad:?} should fail");
        }
    }
}
