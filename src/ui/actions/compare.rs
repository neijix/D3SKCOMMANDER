//! Comparing the two panels' contents or two files.

use std::collections::HashMap;
use std::path::PathBuf;

use crate::core::fs::Entry;
use crate::i18n::tr;
use crate::ui::App;
use crate::ui::compare_view;

impl App {
    pub(super) fn compare_files(&mut self) {
        let mine: Vec<PathBuf> = self
            .target_paths()
            .into_iter()
            .filter(|p| p.is_file())
            .collect();
        let pair = if mine.len() == 2 {
            Some((mine[0].clone(), mine[1].clone()))
        } else {
            let other: Vec<PathBuf> = self.panels[1 - self.active]
                .tab()
                .target_paths()
                .into_iter()
                .filter(|p| p.is_file())
                .collect();
            match (mine.first(), other.first()) {
                (Some(a), Some(b)) if self.active == 0 => Some((a.clone(), b.clone())),
                (Some(a), Some(b)) => Some((b.clone(), a.clone())),
                _ => None,
            }
        };
        match pair {
            Some((a, b)) => self
                .windows
                .push(Box::new(compare_view::CompareWindow::new(a, b))),
            None => self.error(tr("dlg.need_two_files")),
        }
    }

    /// Shift+F2: selects, in both panels, the files missing or newer on the other side.
    pub(super) fn compare_panels(&mut self) {
        let maps: Vec<HashMap<String, Entry>> = self
            .panels
            .iter()
            .map(|p| {
                p.tab()
                    .entries
                    .iter()
                    .filter(|e| !e.is_dir)
                    .map(|e| (e.name.clone(), e.clone()))
                    .collect()
            })
            .collect();
        let mut counts = [0usize; 2];
        for side in 0..2 {
            let (mine, theirs) = (&maps[side], &maps[1 - side]);
            let tab = self.panels[side].tab_mut();
            tab.selected.clear();
            for (name, e) in mine {
                let differs = match theirs.get(name) {
                    None => true,
                    Some(o) => {
                        e.modified > o.modified.map(|m| m + std::time::Duration::from_secs(2))
                    }
                };
                if differs {
                    tab.selected.insert(e.path.clone());
                    counts[side] += 1;
                }
            }
        }
        self.status = format!(
            "{}: {} ← → {}",
            tr("cmd.compare_dirs"),
            counts[0],
            counts[1]
        );
    }
}
