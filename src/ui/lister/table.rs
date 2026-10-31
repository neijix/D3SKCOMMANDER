//! Tabular lister content: DBF records and disk catalogs.

use std::io::Read;
use std::path::Path;

use crate::core::catalog::read_catalog;
use crate::core::dbf::read_dbf;
use crate::core::fs::{grouped, human_size};
use crate::i18n::tr;

/// Records shown at most for a DBF file.
const MAX_DBF_RECORDS: usize = 5000;
/// Larger catalogs are shown as plain text instead of being parsed on the UI thread.
const MAX_CATALOG_SIZE: u64 = 64 * 1024 * 1024;

pub struct Table {
    pub columns: Vec<String>,
    /// Width hint per column, in characters.
    pub widths: Vec<usize>,
    pub rows: Vec<Vec<String>>,
    /// Shown above the table (record counts, catalog root).
    pub note: String,
}

impl Table {
    /// Indices of the rows containing `filter` (case-insensitive) in any cell.
    pub fn filtered(&self, filter: &str) -> Vec<usize> {
        let f = filter.trim().to_lowercase();
        (0..self.rows.len())
            .filter(|&i| f.is_empty() || self.rows[i].iter().any(|c| c.to_lowercase().contains(&f)))
            .collect()
    }
}

pub fn load_dbf(path: &Path) -> std::io::Result<Table> {
    let t = read_dbf(path, MAX_DBF_RECORDS)?;
    let mut columns = vec!["#".to_string()];
    let mut widths = vec![6];
    for f in &t.fields {
        columns.push(f.name.clone());
        widths.push((f.length as usize).clamp(f.name.chars().count(), 40));
    }
    let rows = t
        .records
        .iter()
        .zip(&t.deleted)
        .enumerate()
        .map(|(i, (rec, &del))| {
            let mut row = vec![format!("{}{}", i + 1, if del { " *" } else { "" })];
            row.extend(rec.iter().map(|c| c.trim_end().to_string()));
            row
        })
        .collect();
    let shown = t.records.len();
    let total = t.total_records as usize;
    let note = if shown < total {
        format!("{shown} / {total}")
    } else {
        total.to_string()
    };
    Ok(Table {
        columns,
        widths,
        rows,
        note,
    })
}

/// True when the file looks like a catalog: a root line, then tab-separated entries.
fn looks_like_catalog(path: &Path) -> bool {
    let Ok(f) = std::fs::File::open(path) else {
        return false;
    };
    let mut head = Vec::new();
    if f.take(4096).read_to_end(&mut head).is_err() {
        return false;
    }
    let text = String::from_utf8_lossy(&head);
    let mut lines = text.lines();
    lines
        .next()
        .is_some_and(|l| !l.is_empty() && !l.contains('\t'))
        && lines
            .find(|l| !l.trim().is_empty())
            .is_some_and(|l| l.contains('\t'))
}

/// A catalog table for `.cat` / `.dcat` files that parse as one, `None` otherwise.
pub fn load_catalog(path: &Path) -> Option<Table> {
    let ext = path.extension()?.to_string_lossy().to_lowercase();
    if !matches!(ext.as_str(), "cat" | "dcat") || !looks_like_catalog(path) {
        return None;
    }
    if std::fs::metadata(path).ok()?.len() > MAX_CATALOG_SIZE {
        return None;
    }
    let (root, entries) = read_catalog(path).ok()?;
    let rows = entries
        .iter()
        .map(|e| {
            let name = if e.is_dir {
                format!("{}\\", e.path)
            } else {
                e.path.clone()
            };
            let size = if e.is_dir {
                "<DIR>".to_string()
            } else {
                format!("{} ({})", grouped(e.size), human_size(e.size))
            };
            vec![name, size, e.modified.clone().unwrap_or_default()]
        })
        .collect();
    Some(Table {
        columns: vec![tr("path").into(), tr("size").into(), tr("date").into()],
        widths: vec![60, 22, 19],
        rows,
        note: root,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn catalog_is_detected_and_filtered() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("disk.cat");
        std::fs::write(
            &p,
            "C:\\data\nsub\\\t0\t2024.01.02\t10:11.12\nsub\\a.txt\t1234\t2024.01.02\t10:11.13\n",
        )
        .unwrap();
        let t = load_catalog(&p).expect("catalog");
        assert_eq!(t.note, "C:\\data");
        assert_eq!(t.rows.len(), 2);
        assert_eq!(t.rows[0][1], "<DIR>");
        assert_eq!(t.rows[1][2], "2024.01.02 10:11.13");
        assert_eq!(t.filtered("A.TXT"), vec![1]);
        assert_eq!(t.filtered("").len(), 2);
    }

    #[test]
    fn plain_text_named_cat_is_not_a_catalog() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("meow.cat");
        std::fs::write(&p, "just a cat story\nwithout tabs\n").unwrap();
        assert!(load_catalog(&p).is_none());
        let other = dir.path().join("x.txt");
        std::fs::write(&other, "C:\\\na\t1\t2024.01.01\t00:00.00\n").unwrap();
        assert!(load_catalog(&other).is_none());
    }
}
