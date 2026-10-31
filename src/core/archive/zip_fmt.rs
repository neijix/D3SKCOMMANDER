//! ZIP via the `zip` crate (deflate).

use std::fs::File;
use std::io::{self, BufReader, BufWriter, Write};
use std::path::Path;
use std::time::SystemTime;

use chrono::{Datelike, Local, NaiveDate, Timelike};
use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, DateTime, ZipArchive, ZipWriter};

use super::ArchiveEntry;
use super::common::{EntryMeta, Plan, SrcItem, Step, extract_entry, normalize, with_implied_dirs};

fn to_system_time(dt: DateTime) -> Option<SystemTime> {
    let naive = NaiveDate::from_ymd_opt(
        i32::from(dt.year()),
        u32::from(dt.month()),
        u32::from(dt.day()),
    )?
    .and_hms_opt(
        u32::from(dt.hour()),
        u32::from(dt.minute()),
        u32::from(dt.second()),
    )?;
    Some(naive.and_local_timezone(Local).earliest()?.into())
}

fn from_system_time(t: SystemTime) -> Option<DateTime> {
    let local = chrono::DateTime::<Local>::from(t);
    DateTime::from_date_and_time(
        u16::try_from(local.year()).ok()?,
        u8::try_from(local.month()).ok()?,
        u8::try_from(local.day()).ok()?,
        u8::try_from(local.hour()).ok()?,
        u8::try_from(local.minute()).ok()?,
        u8::try_from(local.second()).ok()?,
    )
    .ok()
}

fn open(archive: &Path) -> io::Result<ZipArchive<BufReader<File>>> {
    Ok(ZipArchive::new(BufReader::new(File::open(archive)?))?)
}

pub(super) fn list(archive: &Path) -> io::Result<Vec<ArchiveEntry>> {
    let mut za = open(archive)?;
    let mut out = Vec::with_capacity(za.len());
    for i in 0..za.len() {
        let f = za.by_index_raw(i)?;
        let path = normalize(&f.name()?);
        if path.is_empty() {
            continue;
        }
        out.push(ArchiveEntry {
            path,
            size: f.size(),
            compressed_size: Some(f.compressed_size()),
            is_dir: f.is_dir(),
            modified: f.last_modified().and_then(to_system_time),
        });
    }
    Ok(with_implied_dirs(out))
}

pub(super) fn extract(
    archive: &Path,
    plan: &Plan,
    dest: &Path,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<usize> {
    let mut za = open(archive)?;
    let mut count = 0;
    for i in 0..za.len() {
        let mut f = za.by_index(i)?;
        if f.is_symlink() {
            continue; // never recreate links from an archive
        }
        let raw = f.name()?.into_owned();
        let meta = EntryMeta {
            is_dir: f.is_dir(),
            modified: f.last_modified().and_then(to_system_time),
            mode: f.unix_mode(),
        };
        match extract_entry(plan, dest, &raw, &meta, &mut f, progress)? {
            Step::File => count += 1,
            Step::Stop => break,
            Step::Dir | Step::Skipped => {}
        }
    }
    Ok(count)
}

fn options_for(item: &SrcItem, level: u8) -> SimpleFileOptions {
    let mut opts = SimpleFileOptions::default().large_file(item.size >= u64::from(u32::MAX));
    opts = if level == 0 {
        opts.compression_method(CompressionMethod::Stored)
    } else {
        opts.compression_method(CompressionMethod::Deflated)
            .compression_level(Some(i64::from(level.min(9))))
    };
    if let Some(dt) = item.modified.and_then(from_system_time) {
        opts = opts.last_modified_time(dt);
    }
    if let Some(mode) = item.mode {
        opts = opts.unix_permissions(mode);
    }
    opts
}

/// `Ok(None)` = cancelled by the progress callback.
pub(super) fn create(
    archive: &Path,
    items: &[SrcItem],
    level: u8,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<Option<usize>> {
    let mut zip = ZipWriter::new(BufWriter::new(File::create(archive)?));
    let mut count = 0;
    for item in items {
        if !progress(&item.name) {
            return Ok(None);
        }
        let opts = options_for(item, level);
        if item.is_dir {
            zip.add_directory(item.name.as_str(), opts)?;
        } else {
            zip.start_file(item.name.as_str(), opts)?;
            io::copy(&mut File::open(&item.path)?, &mut zip)?;
            count += 1;
        }
    }
    zip.finish()?.flush()?;
    Ok(Some(count))
}
