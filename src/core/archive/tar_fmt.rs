//! TAR and TAR.GZ via `tar` + `flate2`.

use std::fs::File;
use std::io::{self, BufReader, BufWriter, Read, Write};
use std::path::Path;
use std::time::{Duration, SystemTime};

use flate2::Compression;
use flate2::read::GzDecoder;
use flate2::write::GzEncoder;
use tar::EntryType;

use super::ArchiveEntry;
use super::common::{EntryMeta, Plan, SrcItem, Step, extract_entry, normalize, with_implied_dirs};

fn open(archive: &Path, gz: bool) -> io::Result<tar::Archive<Box<dyn Read>>> {
    let file = BufReader::new(File::open(archive)?);
    let reader: Box<dyn Read> = if gz {
        Box::new(GzDecoder::new(file))
    } else {
        Box::new(file)
    };
    Ok(tar::Archive::new(reader))
}

fn mtime(secs: u64) -> Option<SystemTime> {
    (secs > 0).then(|| SystemTime::UNIX_EPOCH + Duration::from_secs(secs))
}

fn is_file_like(kind: EntryType) -> bool {
    matches!(
        kind,
        EntryType::Regular | EntryType::Continuous | EntryType::GNUSparse
    )
}

pub(super) fn list(archive: &Path, gz: bool) -> io::Result<Vec<ArchiveEntry>> {
    let mut ar = open(archive, gz)?;
    let mut out = Vec::new();
    for entry in ar.entries()? {
        let entry = entry?;
        let kind = entry.header().entry_type();
        let is_dir = kind == EntryType::Directory;
        let is_link = matches!(kind, EntryType::Symlink | EntryType::Link);
        if !(is_dir || is_link || is_file_like(kind)) {
            continue;
        }
        let path = normalize(&String::from_utf8_lossy(&entry.path_bytes()));
        if path.is_empty() {
            continue;
        }
        out.push(ArchiveEntry {
            path,
            size: if is_link { 0 } else { entry.size() },
            compressed_size: None,
            is_dir,
            modified: entry.header().mtime().ok().and_then(mtime),
        });
    }
    Ok(with_implied_dirs(out))
}

pub(super) fn extract(
    archive: &Path,
    gz: bool,
    plan: &Plan,
    dest: &Path,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<usize> {
    let mut ar = open(archive, gz)?;
    let mut count = 0;
    for entry in ar.entries()? {
        let mut entry = entry?;
        let kind = entry.header().entry_type();
        let is_dir = kind == EntryType::Directory;
        if !(is_dir || is_file_like(kind)) {
            continue; // links and devices are never recreated
        }
        let raw = String::from_utf8_lossy(&entry.path_bytes()).into_owned();
        let meta = EntryMeta {
            is_dir,
            modified: entry.header().mtime().ok().and_then(mtime),
            mode: entry.header().mode().ok(),
        };
        match extract_entry(plan, dest, &raw, &meta, &mut entry, progress)? {
            Step::File => count += 1,
            Step::Stop => break,
            Step::Dir | Step::Skipped => {}
        }
    }
    Ok(count)
}

fn write_items<W: Write>(
    out: W,
    items: &[SrcItem],
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<Option<(W, usize)>> {
    let mut builder = tar::Builder::new(out);
    let mut count = 0;
    for item in items {
        if !progress(&item.name) {
            return Ok(None);
        }
        if item.is_dir {
            builder.append_dir(&item.name, &item.path)?;
        } else {
            builder.append_path_with_name(&item.path, &item.name)?;
            count += 1;
        }
    }
    Ok(Some((builder.into_inner()?, count)))
}

/// `Ok(None)` = cancelled by the progress callback.
pub(super) fn create(
    archive: &Path,
    gz: bool,
    items: &[SrcItem],
    level: u8,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<Option<usize>> {
    let file = BufWriter::new(File::create(archive)?);
    if gz {
        let enc = GzEncoder::new(file, Compression::new(u32::from(level.min(9))));
        let Some((enc, count)) = write_items(enc, items, progress)? else {
            return Ok(None);
        };
        enc.finish()?.flush()?;
        Ok(Some(count))
    } else {
        let Some((mut file, count)) = write_items(file, items, progress)? else {
            return Ok(None);
        };
        file.flush()?;
        Ok(Some(count))
    }
}
