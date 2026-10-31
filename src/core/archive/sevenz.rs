//! 7Z via `sevenz-rust2` (LZMA2).

use std::fs::File;
use std::io;
use std::path::Path;
use std::time::{Duration, SystemTime};

use sevenz_rust2::{
    ArchiveEntry as SzEntry, ArchiveReader, ArchiveWriter, EncoderConfiguration, EncoderMethod,
    Error as SzError, Password, encoder_options::Lzma2Options,
};

use super::ArchiveEntry;
use super::common::{EntryMeta, Plan, SrcItem, Step, extract_entry, normalize, with_implied_dirs};

/// 100 ns ticks between 1601-01-01 and the Unix epoch.
const NT_UNIX_EPOCH_SECS: u64 = 11_644_473_600;

fn to_io(e: SzError) -> io::Error {
    match e {
        SzError::Io(e, _) | SzError::FileOpen(e, _) | SzError::MaybeBadPassword(e) => e,
        SzError::PasswordRequired => io::Error::new(
            io::ErrorKind::PermissionDenied,
            "the archive is password protected",
        ),
        other => io::Error::other(format!("7z: {other}")),
    }
}

fn modified(e: &SzEntry) -> Option<SystemTime> {
    if !e.has_last_modified_date {
        return None;
    }
    let ticks = u64::from(e.last_modified_date());
    let unix_secs = (ticks / 10_000_000).checked_sub(NT_UNIX_EPOCH_SECS)?;
    let nanos = u32::try_from((ticks % 10_000_000) * 100).ok()?;
    SystemTime::UNIX_EPOCH.checked_add(Duration::new(unix_secs, nanos))
}

/// Symlinks (Unix mode in the high attribute bits) and Windows reparse points.
fn is_link(e: &SzEntry) -> bool {
    let attrs = e.windows_attributes();
    let unix_symlink = attrs & 0x8000 != 0 && (attrs >> 16) & 0o170000 == 0o120000;
    unix_symlink || attrs & 0x400 != 0
}

fn open(archive: &Path) -> io::Result<ArchiveReader<File>> {
    ArchiveReader::open(archive, Password::empty()).map_err(to_io)
}

pub(super) fn list(archive: &Path) -> io::Result<Vec<ArchiveEntry>> {
    let reader = open(archive)?;
    let mut out = Vec::new();
    for e in &reader.archive().files {
        let path = normalize(e.name());
        if path.is_empty() || e.is_anti_item() {
            continue;
        }
        out.push(ArchiveEntry {
            path,
            size: e.size(),
            compressed_size: (e.compressed_size > 0).then_some(e.compressed_size),
            is_dir: e.is_directory(),
            modified: modified(e),
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
    let mut reader = open(archive)?;
    let mut count = 0;
    let mut failure: Option<io::Error> = None;
    let result = reader.for_each_entries(|e, data| {
        if e.is_anti_item() || is_link(e) {
            return Ok(true);
        }
        let meta = EntryMeta {
            is_dir: e.is_directory(),
            modified: modified(e),
            mode: None,
        };
        match extract_entry(plan, dest, e.name(), &meta, data, progress) {
            Ok(Step::File) => count += 1,
            Ok(Step::Stop) => return Ok(false),
            Ok(Step::Dir | Step::Skipped) => {}
            Err(err) => {
                failure = Some(err);
                return Ok(false);
            }
        }
        Ok(true)
    });
    if let Some(err) = failure {
        return Err(err);
    }
    result.map_err(to_io)?;
    Ok(count)
}

fn encoder(level: u8) -> EncoderConfiguration {
    if level == 0 {
        EncoderConfiguration::new(EncoderMethod::COPY)
    } else {
        Lzma2Options::from_level(u32::from(level.min(9))).into()
    }
}

/// `Ok(None)` = cancelled by the progress callback.
pub(super) fn create(
    archive: &Path,
    items: &[SrcItem],
    level: u8,
    progress: &mut dyn FnMut(&str) -> bool,
) -> io::Result<Option<usize>> {
    let mut writer = ArchiveWriter::create(archive).map_err(to_io)?;
    writer.set_content_methods(vec![encoder(level)]);
    let mut count = 0;
    for item in items {
        if !progress(&item.name) {
            return Ok(None);
        }
        let entry = SzEntry::from_path(&item.path, item.name.clone());
        let data = if item.is_dir {
            None
        } else {
            Some(File::open(&item.path)?)
        };
        writer.push_archive_entry(entry, data).map_err(to_io)?;
        if !item.is_dir {
            count += 1;
        }
    }
    writer.finish()?;
    Ok(Some(count))
}
