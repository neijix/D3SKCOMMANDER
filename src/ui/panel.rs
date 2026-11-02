//! Panel state: tabs, location (folder, archive or a search result list), cursor,
//! selection, sorting, quick filter and history. No rendering here.

use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::AtomicBool;
use std::time::{Duration, Instant};

use crate::core::archive::{self, ArchiveEntry, ArchiveKind};
use crate::core::fs::{self as cfs, Entry, SortKey, SortOrder};
use crate::core::wildcard::Mask;

/// Where a tab points.
#[derive(Clone, Debug, PartialEq)]
pub enum Location {
    Local(PathBuf),
    /// Inside an archive: `inner` is a '/'-separated folder ("" = archive root).
    Archive {
        file: PathBuf,
        inner: String,
    },
    /// A fixed list of paths, e.g. search results fed to the panel.
    List {
        title: String,
        paths: Arc<Vec<PathBuf>>,
    },
}

impl Location {
    /// Real folder for operations (the archive's folder when inside an archive).
    pub fn base_dir(&self) -> Option<PathBuf> {
        match self {
            Location::Local(p) => Some(p.clone()),
            Location::Archive { file, .. } => file.parent().map(Path::to_path_buf),
            Location::List { .. } => None,
        }
    }

    pub fn title(&self) -> String {
        match self {
            Location::Local(p) => p
                .file_name()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_else(|| p.to_string_lossy().into_owned()),
            Location::Archive { file, inner } => {
                let name = file.file_name().unwrap_or_default().to_string_lossy();
                match inner.rsplit('/').next() {
                    Some(last) if !last.is_empty() => format!("{name}:{last}"),
                    _ => name.into_owned(),
                }
            }
            Location::List { title, .. } => title.clone(),
        }
    }

    pub fn display(&self) -> String {
        match self {
            Location::Local(p) => p.to_string_lossy().into_owned(),
            Location::Archive { file, inner } => {
                format!("{}/{}", file.to_string_lossy(), inner)
            }
            Location::List { title, paths } => format!("{title} ({})", paths.len()),
        }
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum ViewMode {
    #[default]
    Full,
    Brief,
    Thumbnails,
}

/// One tab of a panel.
pub struct Tab {
    pub location: Location,
    pub entries: Vec<Entry>,
    /// Indices into `entries` that pass the quick filter, in display order.
    pub visible: Vec<usize>,
    /// Index into `visible`.
    pub cursor: usize,
    pub selected: HashSet<PathBuf>,
    pub sort: SortOrder,
    pub filter: Option<String>,
    pub branch: bool,
    pub view: ViewMode,
    pub back: Vec<Location>,
    pub forward: Vec<Location>,
    pub error: Option<String>,
    pub dir_sizes: HashMap<PathBuf, u64>,
    pub scroll_to_cursor: bool,
    pub path_edit: Option<String>,
    archive_cache: Option<(PathBuf, Arc<Vec<ArchiveEntry>>)>,
    type_ahead: String,
    type_ahead_at: Option<Instant>,
}

impl Tab {
    pub fn new(location: Location, sort: SortOrder) -> Tab {
        Tab {
            location,
            entries: Vec::new(),
            visible: Vec::new(),
            cursor: 0,
            selected: HashSet::new(),
            sort,
            filter: None,
            branch: false,
            view: ViewMode::Full,
            back: Vec::new(),
            forward: Vec::new(),
            error: None,
            dir_sizes: HashMap::new(),
            scroll_to_cursor: true,
            path_edit: None,
            archive_cache: None,
            type_ahead: String::new(),
            type_ahead_at: None,
        }
    }

    /// Re-reads the location, keeping the cursor on the same name when possible.
    pub fn reload(&mut self, show_hidden: bool) {
        let keep = self.current().map(|e| e.path.clone());
        self.error = None;
        let listed = match &self.location {
            Location::Local(dir) if self.branch => {
                cfs::list_branch(dir, show_hidden, 200_000, &AtomicBool::new(false))
                    .map_err(|e| e.to_string())
            }
            Location::Local(dir) => cfs::list_dir(dir, show_hidden).map_err(|e| e.to_string()),
            Location::Archive { file, inner } => {
                let (file, inner) = (file.clone(), inner.clone());
                self.list_archive(&file, &inner)
            }
            Location::List { paths, .. } => Ok(list_paths(paths)),
        };
        match listed {
            Ok(entries) => self.entries = entries,
            Err(e) => {
                self.error = Some(e);
                self.entries.clear();
            }
        }
        for e in &mut self.entries {
            if let Some(size) = self.dir_sizes.get(&e.path) {
                e.size = *size;
            }
        }
        cfs::sort_entries(&mut self.entries, self.sort);
        let paths: HashSet<&PathBuf> = self.entries.iter().map(|e| &e.path).collect();
        self.selected.retain(|p| paths.contains(p));
        self.apply_filter();
        if let Some(path) = keep {
            self.select_path(&path);
        }
        self.clamp_cursor();
    }

    fn list_archive(&mut self, file: &Path, inner: &str) -> Result<Vec<Entry>, String> {
        let cached = match &self.archive_cache {
            Some((f, list)) if f == file => list.clone(),
            _ => {
                let list = Arc::new(archive::list(file).map_err(|e| e.to_string())?);
                self.archive_cache = Some((file.to_path_buf(), list.clone()));
                list
            }
        };
        let mut out = vec![Entry {
            name: "..".into(),
            path: PathBuf::new(),
            is_dir: true,
            is_link: false,
            is_parent: true,
            size: 0,
            modified: None,
            hidden: false,
            readonly: false,
            attrs: String::new(),
        }];
        for e in archive::list_dir(&cached, inner) {
            let name = e
                .path
                .trim_end_matches('/')
                .rsplit('/')
                .next()
                .unwrap_or("");
            out.push(Entry {
                name: name.to_string(),
                path: PathBuf::from(e.path.trim_end_matches('/')),
                is_dir: e.is_dir,
                is_link: false,
                is_parent: false,
                size: e.size,
                modified: e.modified,
                hidden: false,
                readonly: false,
                attrs: String::new(),
            });
        }
        Ok(out)
    }

    pub fn apply_filter(&mut self) {
        let mask = self.filter.as_deref().map(quick_filter_mask);
        self.visible = (0..self.entries.len())
            .filter(|&i| {
                let e = &self.entries[i];
                e.is_parent || mask.as_ref().is_none_or(|m| m.matches(&e.name))
            })
            .collect();
        self.clamp_cursor();
    }

    pub fn clamp_cursor(&mut self) {
        if self.cursor >= self.visible.len() {
            self.cursor = self.visible.len().saturating_sub(1);
        }
    }

    pub fn current(&self) -> Option<&Entry> {
        self.visible
            .get(self.cursor)
            .and_then(|&i| self.entries.get(i))
    }

    pub fn visible_entries(&self) -> impl Iterator<Item = &Entry> {
        self.visible.iter().filter_map(|&i| self.entries.get(i))
    }

    pub fn select_path(&mut self, path: &Path) {
        let found = self.visible_entries().position(|e| e.path == path);
        if let Some(pos) = found {
            self.cursor = pos;
            self.scroll_to_cursor = true;
        }
    }

    /// The selection, or the entry under the cursor when nothing is selected.
    pub fn targets(&self) -> Vec<Entry> {
        let selected: Vec<Entry> = self
            .visible_entries()
            .filter(|e| self.selected.contains(&e.path))
            .cloned()
            .collect();
        if !selected.is_empty() {
            return selected;
        }
        self.current()
            .filter(|e| !e.is_parent)
            .cloned()
            .into_iter()
            .collect()
    }

    pub fn target_paths(&self) -> Vec<PathBuf> {
        self.targets().into_iter().map(|e| e.path).collect()
    }

    pub fn toggle_selection(&mut self, path: &Path) {
        if !self.selected.remove(path) {
            self.selected.insert(path.to_path_buf());
        }
    }

    /// A press on the row at `index`, which takes the cursor: Shift adds the range from the
    /// cursor, Ctrl toggles that entry, a plain press drops the selection.
    pub fn click(&mut self, index: usize, ctrl: bool, shift: bool) {
        let Some(&at) = self.visible.get(index) else {
            return;
        };
        if shift {
            let from = self.cursor.min(self.visible.len() - 1);
            let (a, b) = (from.min(index), from.max(index));
            let paths: Vec<PathBuf> = self.visible[a..=b]
                .iter()
                .map(|&i| &self.entries[i])
                .filter(|e| !e.is_parent)
                .map(|e| e.path.clone())
                .collect();
            self.selected.extend(paths);
        } else if ctrl {
            let e = &self.entries[at];
            if !e.is_parent {
                let p = e.path.clone();
                self.toggle_selection(&p);
            }
        } else {
            self.selected.clear();
        }
        self.cursor = index;
    }

    /// A right press on the row at `index`: the selection stays when that entry is part of
    /// it, else it is dropped so the context menu acts on that entry alone.
    pub fn point(&mut self, index: usize) {
        let Some(&at) = self.visible.get(index) else {
            return;
        };
        if !self.selected.contains(&self.entries[at].path) {
            self.selected.clear();
        }
        self.cursor = index;
    }

    pub fn move_cursor(&mut self, delta: isize) {
        let len = self.visible.len() as isize;
        if len == 0 {
            return;
        }
        let next = (self.cursor as isize + delta).clamp(0, len - 1);
        self.cursor = next as usize;
        self.scroll_to_cursor = true;
    }

    /// Selects (`select = true`) or unselects every visible entry matching `mask`.
    pub fn select_mask(&mut self, mask: &str, select: bool, include_dirs: bool) {
        let mask = Mask::new(mask);
        let paths: Vec<PathBuf> = self
            .visible_entries()
            .filter(|e| !e.is_parent && (include_dirs || !e.is_dir) && mask.matches(&e.name))
            .map(|e| e.path.clone())
            .collect();
        for p in paths {
            if select {
                self.selected.insert(p);
            } else {
                self.selected.remove(&p);
            }
        }
    }

    pub fn select_all(&mut self) {
        let paths: Vec<PathBuf> = self
            .visible_entries()
            .filter(|e| !e.is_parent)
            .map(|e| e.path.clone())
            .collect();
        self.selected.extend(paths);
    }

    pub fn invert_selection(&mut self) {
        let paths: Vec<PathBuf> = self
            .visible_entries()
            .filter(|e| !e.is_parent && !e.is_dir)
            .map(|e| e.path.clone())
            .collect();
        for p in paths {
            self.toggle_selection(&p);
        }
    }

    /// Navigates, recording history. Returns false when the target can't be listed.
    pub fn go(&mut self, location: Location, show_hidden: bool) -> bool {
        let previous = std::mem::replace(&mut self.location, location);
        let came_from = match &previous {
            Location::Local(p) => Some(p.clone()),
            Location::Archive { file, inner } if inner.is_empty() => Some(file.clone()),
            Location::Archive { file, inner } => {
                Some(PathBuf::from(format!("{}/{}", file.display(), inner)))
            }
            Location::List { .. } => None,
        };
        self.filter = None;
        self.selected.clear();
        self.cursor = 0;
        self.reload(show_hidden);
        if self.error.is_some() && !matches!(previous, Location::List { .. }) {
            let err = self.error.take();
            self.location = previous;
            self.reload(show_hidden);
            self.error = err;
            return false;
        }
        self.back.push(previous);
        self.forward.clear();
        if let Some(from) = came_from {
            self.select_came_from(&from);
        }
        true
    }

    fn select_came_from(&mut self, from: &Path) {
        // Going up: put the cursor on the folder (or archive) we came out of.
        let hit = self.visible_entries().position(|e| {
            !e.is_parent
                && (e.path == from || from.ends_with(&e.path) && !e.path.as_os_str().is_empty())
        });
        if let Some(pos) = hit {
            self.cursor = pos;
            self.scroll_to_cursor = true;
        }
    }

    pub fn back(&mut self, show_hidden: bool) {
        if let Some(prev) = self.back.pop() {
            let cur = std::mem::replace(&mut self.location, prev);
            self.forward.push(cur);
            self.cursor = 0;
            self.reload(show_hidden);
        }
    }

    pub fn forward(&mut self, show_hidden: bool) {
        if let Some(next) = self.forward.pop() {
            let cur = std::mem::replace(&mut self.location, next);
            self.back.push(cur);
            self.cursor = 0;
            self.reload(show_hidden);
        }
    }

    /// The location one level up, if any.
    pub fn parent_location(&self) -> Option<Location> {
        match &self.location {
            Location::Local(p) => p.parent().map(|p| Location::Local(p.to_path_buf())),
            Location::Archive { file, inner } if inner.is_empty() => {
                file.parent().map(|p| Location::Local(p.to_path_buf()))
            }
            Location::Archive { file, inner } => Some(Location::Archive {
                file: file.clone(),
                inner: inner
                    .rsplit_once('/')
                    .map(|(a, _)| a.to_string())
                    .unwrap_or_default(),
            }),
            Location::List { .. } => self.back.last().cloned(),
        }
    }

    /// Where Enter on `entry` leads (folder, archive folder or archive root).
    pub fn enter_location(&self, entry: &Entry) -> Option<Location> {
        if entry.is_parent {
            return self.parent_location();
        }
        match &self.location {
            Location::Archive { file, .. } if entry.is_dir => Some(Location::Archive {
                file: file.clone(),
                inner: entry.path.to_string_lossy().replace('\\', "/"),
            }),
            Location::Archive { .. } => None,
            _ if entry.is_dir => Some(Location::Local(entry.path.clone())),
            _ if is_browsable_archive(&entry.path) => Some(Location::Archive {
                file: entry.path.clone(),
                inner: String::new(),
            }),
            _ => None,
        }
    }

    pub fn set_sort(&mut self, key: SortKey) {
        if self.sort.key == key {
            self.sort.descending = !self.sort.descending;
        } else {
            self.sort = SortOrder {
                key,
                descending: false,
            };
        }
        let keep = self.current().map(|e| e.path.clone());
        cfs::sort_entries(&mut self.entries, self.sort);
        self.apply_filter();
        if let Some(p) = keep {
            self.select_path(&p);
        }
    }

    /// Type-ahead: jumps to the next name starting with the typed letters.
    pub fn type_ahead(&mut self, text: &str) {
        let fresh = self
            .type_ahead_at
            .is_none_or(|t| t.elapsed() > Duration::from_millis(900));
        if fresh {
            self.type_ahead.clear();
        }
        self.type_ahead.push_str(&text.to_lowercase());
        self.type_ahead_at = Some(Instant::now());
        let prefix = self.type_ahead.clone();
        let hit = self
            .visible_entries()
            .position(|e| !e.is_parent && e.name.to_lowercase().starts_with(&prefix));
        if let Some(pos) = hit {
            self.cursor = pos;
            self.scroll_to_cursor = true;
        }
    }

    pub fn selection_summary(&self) -> (usize, u64, usize, u64) {
        let mut sel = (0usize, 0u64);
        let mut all = (0usize, 0u64);
        for e in self.visible_entries().filter(|e| !e.is_parent) {
            all.0 += 1;
            all.1 += e.size;
            if self.selected.contains(&e.path) {
                sel.0 += 1;
                sel.1 += e.size;
            }
        }
        (sel.0, sel.1, all.0, all.1)
    }
}

/// Quick filter text → mask: plain text means "contains".
fn quick_filter_mask(text: &str) -> Mask {
    if text.contains(['*', '?']) {
        Mask::new(text)
    } else {
        Mask::new(&format!("*{text}*"))
    }
}

pub fn is_browsable_archive(path: &Path) -> bool {
    match ArchiveKind::from_path(path) {
        Some(ArchiveKind::External) => archive::external_tool().is_some(),
        Some(_) => true,
        None => false,
    }
}

fn list_paths(paths: &[PathBuf]) -> Vec<Entry> {
    paths
        .iter()
        .filter_map(|p| cfs::entry_from_path(p).ok())
        .map(|mut e| {
            e.name = e.path.to_string_lossy().into_owned();
            e
        })
        .collect()
}

/// The two panels; each owns its tabs.
pub struct PanelState {
    pub tabs: Vec<Tab>,
    pub active_tab: usize,
}

impl PanelState {
    pub fn new(paths: &[PathBuf], active: usize, sort: SortOrder, show_hidden: bool) -> Self {
        let mut tabs: Vec<Tab> = paths
            .iter()
            .filter(|p| p.is_dir())
            .map(|p| Tab::new(Location::Local(p.clone()), sort))
            .collect();
        if tabs.is_empty() {
            tabs.push(Tab::new(Location::Local(cfs::home_dir()), sort));
        }
        for t in &mut tabs {
            t.reload(show_hidden);
        }
        let active_tab = active.min(tabs.len() - 1);
        PanelState { tabs, active_tab }
    }

    pub fn tab(&self) -> &Tab {
        &self.tabs[self.active_tab]
    }

    pub fn tab_mut(&mut self) -> &mut Tab {
        &mut self.tabs[self.active_tab]
    }

    pub fn new_tab(&mut self, show_hidden: bool) {
        let mut t = Tab::new(self.tab().location.clone(), self.tab().sort);
        t.reload(show_hidden);
        self.tabs.insert(self.active_tab + 1, t);
        self.active_tab += 1;
    }

    pub fn close_tab(&mut self) {
        if self.tabs.len() > 1 {
            self.tabs.remove(self.active_tab);
            self.active_tab = self.active_tab.min(self.tabs.len() - 1);
        }
    }

    pub fn next_tab(&mut self) {
        self.active_tab = (self.active_tab + 1) % self.tabs.len();
    }

    /// Local folders of every tab, for the settings file.
    pub fn tab_dirs(&self) -> Vec<PathBuf> {
        self.tabs
            .iter()
            .filter_map(|t| match &t.location {
                Location::Local(p) => Some(p.clone()),
                Location::Archive { file, .. } => file.parent().map(Path::to_path_buf),
                Location::List { .. } => None,
            })
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn tab_in(dir: &Path) -> Tab {
        let mut t = Tab::new(Location::Local(dir.to_path_buf()), SortOrder::default());
        t.reload(false);
        t
    }

    #[test]
    fn navigation_history_and_cursor_on_parent() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir(dir.path().join("sub")).unwrap();
        fs::write(dir.path().join("a.txt"), "x").unwrap();
        let mut t = tab_in(dir.path());
        let sub = t
            .visible_entries()
            .find(|e| e.name == "sub")
            .unwrap()
            .clone();
        let loc = t.enter_location(&sub).unwrap();
        assert!(t.go(loc, false));
        assert_eq!(t.location, Location::Local(dir.path().join("sub")));
        let up = t.parent_location().unwrap();
        t.go(up, false);
        assert_eq!(t.current().unwrap().name, "sub");
        t.back(false);
        assert_eq!(t.location, Location::Local(dir.path().join("sub")));
        t.forward(false);
        assert_eq!(t.location, Location::Local(dir.path().to_path_buf()));
    }

    #[test]
    fn go_to_missing_folder_keeps_location() {
        let dir = tempfile::tempdir().unwrap();
        let mut t = tab_in(dir.path());
        assert!(!t.go(Location::Local(dir.path().join("nope")), false));
        assert_eq!(t.location, Location::Local(dir.path().to_path_buf()));
        assert!(t.error.is_some());
    }

    #[test]
    fn selection_filter_and_targets() {
        let dir = tempfile::tempdir().unwrap();
        for n in ["a.txt", "b.txt", "c.rs"] {
            fs::write(dir.path().join(n), "x").unwrap();
        }
        let mut t = tab_in(dir.path());
        t.select_mask("*.txt", true, false);
        assert_eq!(t.targets().len(), 2);
        t.invert_selection();
        assert_eq!(t.targets()[0].name, "c.rs");
        t.selected.clear();
        t.filter = Some("b".into());
        t.apply_filter();
        assert_eq!(t.visible_entries().filter(|e| !e.is_parent).count(), 1);
        t.filter = None;
        t.apply_filter();
        t.type_ahead("c");
        assert_eq!(t.current().unwrap().name, "c.rs");
        assert_eq!(t.targets()[0].name, "c.rs");
    }

    #[test]
    fn mouse_selects_and_unselects() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir(dir.path().join("d")).unwrap();
        for n in ["a.txt", "b.txt"] {
            fs::write(dir.path().join(n), "x").unwrap();
        }
        let mut t = tab_in(dir.path());
        let at = |t: &Tab, name: &str| t.visible_entries().position(|e| e.name == name).unwrap();
        let names = |t: &Tab| {
            let mut v: Vec<String> = t.targets().into_iter().map(|e| e.name).collect();
            v.sort();
            v
        };
        let (d, a, b) = (at(&t, "d"), at(&t, "a.txt"), at(&t, "b.txt"));
        // Ctrl selects a folder and a file, Ctrl again unselects.
        t.click(d, true, false);
        t.click(a, true, false);
        assert_eq!(names(&t), ["a.txt", "d"]);
        t.click(d, true, false);
        assert_eq!(names(&t), ["a.txt"]);
        // A right press inside the selection keeps it; outside it drops it.
        t.click(b, true, false);
        t.point(a);
        assert_eq!(names(&t), ["a.txt", "b.txt"]);
        t.point(d);
        assert!(t.selected.is_empty());
        // Shift adds the range from the cursor; a plain press unselects everything.
        t.click(b, false, true);
        assert_eq!(t.selected.len(), b - d + 1);
        t.click(a, false, false);
        assert!(t.selected.is_empty());
        assert_eq!(t.cursor, a);
    }

    #[test]
    fn browse_zip_like_a_folder() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir(dir.path().join("src")).unwrap();
        fs::write(dir.path().join("src/inner.txt"), "hello").unwrap();
        let zip = dir.path().join("t.zip");
        archive::create(
            &zip,
            ArchiveKind::Zip,
            &[dir.path().join("src")],
            6,
            &mut |_| true,
        )
        .unwrap();
        let mut t = tab_in(dir.path());
        let entry = t
            .visible_entries()
            .find(|e| e.name == "t.zip")
            .unwrap()
            .clone();
        let loc = t.enter_location(&entry).unwrap();
        assert!(t.go(loc, false));
        let folder = t
            .visible_entries()
            .find(|e| e.name == "src")
            .unwrap()
            .clone();
        let loc = t.enter_location(&folder).unwrap();
        assert!(t.go(loc, false));
        assert!(t.visible_entries().any(|e| e.name == "inner.txt"));
        let up = t.parent_location().unwrap();
        assert_eq!(
            up,
            Location::Archive {
                file: zip.clone(),
                inner: String::new()
            }
        );
    }
}
