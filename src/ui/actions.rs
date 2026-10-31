//! Commands: what every menu item, button and shortcut does, plus keyboard navigation.
//! The heavier tools (pack, checksums, encryption …) are in `tools.rs`.

use std::path::{Path, PathBuf};

use eframe::egui::{self, Key};

use super::App;
use super::dialogs::{ConfirmAction, Dialog, InputAction};
use super::keys::{self, Command};
use super::panel::{Location, ViewMode};
use super::{
    compare_view, editor, ftp_view, lister, rename_view, search_view, settings_view, sync_view,
};
use crate::config::HotlistItem;
use crate::core::checksum::Algo;
use crate::core::fileops::{OpKind, OpRequest};
use crate::core::fs::{self as cfs, Entry, SortKey};
use crate::core::launcher::{self, LaunchMode};
use crate::i18n::{t, tr};

impl App {
    /// Shortcuts, then cursor keys, then type-ahead. Only called when no text field and
    /// no dialog has the keyboard.
    pub(crate) fn handle_keys(&mut self, ctx: &egui::Context) {
        if let Some(cmd) = keys::pressed_command(ctx) {
            self.run(cmd, ctx);
            return;
        }
        let (nav, texts) = ctx.input_mut(|i| {
            let shift = i.modifiers.shift;
            let mut nav = Vec::new();
            for (key, n) in [
                (Key::ArrowUp, Nav::Move(-1)),
                (Key::ArrowDown, Nav::Move(1)),
                (Key::PageUp, Nav::Move(-20)),
                (Key::PageDown, Nav::Move(20)),
                (Key::Home, Nav::Move(isize::MIN / 2)),
                (Key::End, Nav::Move(isize::MAX / 2)),
                (Key::Enter, Nav::Enter),
                (Key::Insert, Nav::Insert),
                (Key::Space, Nav::Space),
                (Key::Escape, Nav::Escape),
            ] {
                while i.key_pressed(key) {
                    i.consume_key(i.modifiers, key);
                    nav.push((n, shift));
                }
            }
            let texts: Vec<String> = i
                .events
                .iter()
                .filter_map(|e| match e {
                    egui::Event::Text(t) => Some(t.clone()),
                    _ => None,
                })
                .collect();
            (nav, texts)
        });
        for (n, shift) in nav {
            self.navigate(n, shift, ctx);
        }
        for text in texts {
            match text.as_str() {
                "*" => self.active_panel().tab_mut().invert_selection(),
                "+" | "-" | " " => {}
                s => self.active_panel().tab_mut().type_ahead(s),
            }
        }
    }

    fn navigate(&mut self, nav: Nav, shift: bool, ctx: &egui::Context) {
        let tab = self.active_panel().tab_mut();
        match nav {
            Nav::Move(delta) => {
                if shift {
                    let steps = delta.unsigned_abs().min(tab.visible.len());
                    for _ in 0..steps.max(1) {
                        toggle_cursor(tab);
                        let before = tab.cursor;
                        tab.move_cursor(delta.signum());
                        if tab.cursor == before {
                            break;
                        }
                    }
                } else {
                    tab.move_cursor(delta);
                }
            }
            Nav::Insert => {
                toggle_cursor(tab);
                tab.move_cursor(1);
            }
            Nav::Space => {
                let dir = tab
                    .current()
                    .filter(|e| e.is_dir && !e.is_parent)
                    .map(|e| e.path.clone());
                match dir {
                    Some(d) if !tab.selected.contains(&d) => {
                        tab.toggle_selection(&d);
                        self.compute_dir_sizes(vec![d]);
                    }
                    _ => toggle_cursor(tab),
                }
            }
            Nav::Enter => self.open_current(ctx),
            Nav::Escape => {
                if tab.filter.take().is_some() {
                    tab.apply_filter();
                } else {
                    tab.selected.clear();
                }
            }
        }
    }

    /// Enter: open a folder or an archive, run / open a file with its default app.
    pub(crate) fn open_current(&mut self, ctx: &egui::Context) {
        let hidden = self.show_hidden();
        let tab = self.active_panel().tab_mut();
        let Some(entry) = tab.current().cloned() else {
            return;
        };
        if let Some(loc) = tab.enter_location(&entry) {
            let from = tab.location.clone();
            if tab.go(loc, hidden) && entry.is_parent {
                // Put the cursor back on the folder we came from.
                let back_to = match from {
                    Location::Local(p) => Some(p),
                    Location::Archive { file, inner } if inner.is_empty() => Some(file),
                    Location::Archive { inner, .. } => Some(PathBuf::from(inner)),
                    Location::List { .. } => None,
                };
                if let Some(p) = back_to {
                    tab.select_path(&p);
                }
            }
            return;
        }
        let path = match self.local_copy(&entry) {
            Ok(p) => p,
            Err(e) => return self.error(e),
        };
        if let Err(e) = launcher::open_with_default(&path) {
            self.error(e);
        }
        let _ = ctx;
    }

    /// A real file for `entry`: itself, or a temporary copy when it is inside an archive.
    fn local_copy(&self, entry: &Entry) -> Result<PathBuf, String> {
        let Location::Archive { file, .. } = &self.panels[self.active].tab().location else {
            return Ok(entry.path.clone());
        };
        let dest = std::env::temp_dir()
            .join("d3skcommander")
            .join(format!("{}", std::process::id()));
        std::fs::create_dir_all(&dest).map_err(|e| e.to_string())?;
        let inner = entry.path.to_string_lossy().replace('\\', "/");
        crate::core::archive::extract(file, Some(&[inner]), &dest, &mut |_| true)
            .map_err(|e| e.to_string())?;
        Ok(dest.join(&entry.name))
    }

    pub(crate) fn error(&mut self, text: impl Into<String>) {
        self.dialog = Some(Dialog::message(tr("error"), text));
    }

    pub(crate) fn targets(&self) -> Vec<Entry> {
        self.panels[self.active].tab().targets()
    }

    pub(crate) fn target_paths(&self) -> Vec<PathBuf> {
        self.panels[self.active].tab().target_paths()
    }

    /// Targets that are real files on disk (not inside an archive), or an error message.
    pub(crate) fn local_targets(&mut self) -> Option<Vec<PathBuf>> {
        if matches!(
            self.panels[self.active].tab().location,
            Location::Archive { .. }
        ) {
            self.error(t(
                "Not available inside an archive: unpack first (Alt+F9).",
                "Indisponible dans une archive : décompressez d'abord (Alt+F9).",
            ));
            return None;
        }
        let paths = self.target_paths();
        if paths.is_empty() {
            self.error(tr("dlg.no_selection"));
            return None;
        }
        Some(paths)
    }

    pub(crate) fn local_files(&mut self) -> Option<Vec<PathBuf>> {
        let files: Vec<PathBuf> = self
            .local_targets()?
            .into_iter()
            .filter(|p| p.is_file())
            .collect();
        if files.is_empty() {
            self.error(tr("dlg.no_selection"));
            return None;
        }
        Some(files)
    }

    pub(crate) fn run(&mut self, cmd: Command, ctx: &egui::Context) {
        let hidden = self.show_hidden();
        use Command::*;
        match cmd {
            View => self.view_current(),
            Edit => self.edit_current(),
            EditNew => {
                if let Some(dir) = self.active_dir() {
                    self.dialog = Some(Dialog::input(
                        tr("cmd.edit_new"),
                        tr("dlg.newfile"),
                        "",
                        InputAction::NewFile { dir },
                    ));
                }
            }
            Copy => self.copy_move(OpKind::Copy),
            Move => self.copy_move(OpKind::Move),
            RenameInPlace => self.rename_current(),
            MkDir => {
                if let Some(dir) = self.active_dir() {
                    self.dialog = Some(Dialog::input(
                        tr("cmd.mkdir"),
                        tr("dlg.mkdir"),
                        "",
                        InputAction::MkDir { dir },
                    ));
                }
            }
            Delete => self.delete(false, ctx),
            DeletePermanent => self.delete(true, ctx),
            Pack => self.pack_dialog(),
            Unpack => self.unpack_dialog(),
            BackupZip => self.backup_zip(ctx),
            Properties => self.properties(),
            OpenWith => {
                if let Some(files) = self.local_targets() {
                    self.dialog = Some(Dialog::OpenWith(super::dialogs::OpenWithForm {
                        files,
                        command: String::new(),
                        once_per_file: false,
                    }));
                }
            }
            Quit => ctx.send_viewport_cmd(egui::ViewportCommand::Close),
            SelectGroup | UnselectGroup => {
                let select = cmd == SelectGroup;
                let title = if select {
                    tr("cmd.select_group")
                } else {
                    tr("cmd.unselect_group")
                };
                let label = if select {
                    tr("dlg.select")
                } else {
                    tr("dlg.unselect")
                };
                self.dialog = Some(Dialog::input(
                    title,
                    label,
                    "*.*",
                    InputAction::Select { select },
                ));
            }
            SelectAll => self.active_panel().tab_mut().select_all(),
            UnselectAll => self.active_panel().tab_mut().selected.clear(),
            Invert => self.active_panel().tab_mut().invert_selection(),
            SelectSameExt => {
                let tab = self.active_panel().tab_mut();
                if let Some(ext) = tab.current().map(|e| e.extension().to_string()) {
                    let mask = if ext.is_empty() {
                        "*.".to_string()
                    } else {
                        format!("*.{ext}")
                    };
                    tab.select_mask(&mask, true, false);
                }
            }
            CopyNames | CopyPaths => {
                let text: Vec<String> = self
                    .targets()
                    .iter()
                    .map(|e| {
                        if cmd == CopyNames {
                            e.name.clone()
                        } else {
                            e.path.display().to_string()
                        }
                    })
                    .collect();
                ctx.copy_text(text.join("\n"));
            }
            ComparePanels => self.compare_panels(),
            Search => {
                let root = self.active_dir().unwrap_or_else(cfs::home_dir);
                self.windows
                    .push(Box::new(search_view::SearchWindow::new(root)));
            }
            MultiRename => {
                if let Some(files) = self.local_targets() {
                    self.windows
                        .push(Box::new(rename_view::RenameWindow::new(files)));
                }
            }
            CompareFiles => self.compare_files(),
            Sync => {
                let (Some(a), Some(b)) = (self.active_dir(), self.other_dir()) else {
                    return self.error(t(
                        "Both panels must show folders.",
                        "Les deux panneaux doivent afficher des dossiers.",
                    ));
                };
                let (l, r) = if self.active == 0 { (a, b) } else { (b, a) };
                self.windows
                    .push(Box::new(sync_view::SyncWindow::new(l, r)));
            }
            Split => self.split_dialog(),
            Combine => self.combine_dialog(),
            ChecksumCreate => self.checksum_dialog(),
            ChecksumVerify => self.checksum_verify(ctx),
            AddTime => self.time_dialog(super::dialogs::TimeMode::Shift),
            ReDate => self.time_dialog(super::dialogs::TimeMode::Sequence),
            Touch => self.time_dialog(super::dialogs::TimeMode::Fixed),
            Catalog => self.catalog_dialog(),
            Encrypt => self.crypt_dialog(true),
            Decrypt => self.crypt_dialog(false),
            DirSizes => {
                let dirs: Vec<PathBuf> = self.panels[self.active]
                    .tab()
                    .visible_entries()
                    .filter(|e| e.is_dir && !e.is_parent)
                    .map(|e| e.path.clone())
                    .collect();
                if matches!(self.panels[self.active].tab().location, Location::Local(_)) {
                    self.compute_dir_sizes(dirs);
                }
            }
            Ftp => self.windows.push(Box::new(ftp_view::FtpWindow::new())),
            Passwords => self
                .windows
                .push(Box::new(settings_view::PasswordsWindow::new())),
            QuickView => {
                self.quick_view = match self.quick_view {
                    Some(_) => None,
                    None => Some(super::QuickView {
                        path: None,
                        lister: None,
                    }),
                }
            }
            ViewFull => self.active_panel().tab_mut().view = ViewMode::Full,
            ViewBrief => self.active_panel().tab_mut().view = ViewMode::Brief,
            ViewThumbs => self.active_panel().tab_mut().view = ViewMode::Thumbnails,
            ToggleHidden => {
                self.shared.settings.show_hidden ^= true;
                self.reload_all();
            }
            Branch => {
                let tab = self.active_panel().tab_mut();
                tab.branch ^= true;
                tab.reload(hidden);
            }
            QuickFilter => {
                let tab = self.active_panel().tab_mut();
                if tab.filter.is_none() {
                    tab.filter = Some(String::new());
                }
                self.focus_filter = true;
            }
            Refresh => self.reload_all(),
            Swap => self.panels.swap(0, 1),
            SameDir => {
                let loc = self.panels[self.active].tab().location.clone();
                self.other_panel().tab_mut().go(loc, hidden);
            }
            NewTab => self.active_panel().new_tab(hidden),
            CloseTab => self.active_panel().close_tab(),
            NextTab => self.active_panel().next_tab(),
            Hotlist => self.dialog = Some(Dialog::Hotlist),
            AddHotlist => self.add_hotlist(),
            History => self.dialog = Some(Dialog::History),
            Back => self.active_panel().tab_mut().back(hidden),
            Forward => self.active_panel().tab_mut().forward(hidden),
            Parent => {
                let tab = self.active_panel().tab_mut();
                if let Some(loc) = tab.parent_location() {
                    let from = tab.location.base_dir();
                    let was_local = matches!(tab.location, Location::Local(_));
                    if tab.go(loc, hidden)
                        && was_local
                        && let Some(p) = from
                    {
                        tab.select_path(&p);
                    }
                }
            }
            Root => {
                let dir = self.active_dir().unwrap_or_else(cfs::home_dir);
                let root = dir.ancestors().last().map(Path::to_path_buf).unwrap_or(dir);
                self.active_panel()
                    .tab_mut()
                    .go(Location::Local(root), hidden);
            }
            Home => {
                self.active_panel()
                    .tab_mut()
                    .go(Location::Local(cfs::home_dir()), hidden);
            }
            Terminal => self.open_terminal(),
            Settings => {
                let w = settings_view::SettingsWindow::new(&self.shared.settings);
                self.windows.push(Box::new(w));
            }
            Log => self.windows.push(Box::new(lister::ListerWindow::new(
                crate::config::log_path(),
            ))),
            About => self.dialog = Some(Dialog::About),
            Keys => self.dialog = Some(Dialog::Keys),
            SortName => self.active_panel().tab_mut().set_sort(SortKey::Name),
            SortExt => self.active_panel().tab_mut().set_sort(SortKey::Ext),
            SortDate => self.active_panel().tab_mut().set_sort(SortKey::Modified),
            SortSize => self.active_panel().tab_mut().set_sort(SortKey::Size),
            SwitchPanel => self.active = 1 - self.active,
            DrivesLeft => self.dialog = Some(Dialog::Drives { side: 0 }),
            DrivesRight => self.dialog = Some(Dialog::Drives { side: 1 }),
        }
    }

    pub(crate) fn add_hotlist(&mut self) {
        if let Some(dir) = self.active_dir() {
            let name = dir
                .file_name()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_else(|| dir.display().to_string());
            if !self.shared.settings.hotlist.iter().any(|h| h.path == dir) {
                self.shared
                    .settings
                    .hotlist
                    .push(HotlistItem { name, path: dir });
                self.status = tr("cmd.add_hotlist").into();
            }
        }
    }

    fn view_current(&mut self) {
        let Some(entry) = self.panels[self.active].tab().current().cloned() else {
            return;
        };
        if entry.is_parent {
            return;
        }
        if entry.is_dir {
            if matches!(self.panels[self.active].tab().location, Location::Local(_)) {
                self.compute_dir_sizes(vec![entry.path]);
            }
            return;
        }
        match self.local_copy(&entry) {
            Ok(p) => self.windows.push(Box::new(lister::ListerWindow::new(p))),
            Err(e) => self.error(e),
        }
    }

    fn edit_current(&mut self) {
        let Some(entry) = self.panels[self.active].tab().current().cloned() else {
            return;
        };
        if entry.is_dir || entry.is_parent {
            return;
        }
        if matches!(
            self.panels[self.active].tab().location,
            Location::Archive { .. }
        ) {
            return self.error(t(
                "Files inside an archive are read-only: unpack them first.",
                "Les fichiers d'une archive sont en lecture seule : décompressez-les d'abord.",
            ));
        }
        self.edit_file(entry.path);
    }

    /// F4: the configured external editor, else the built-in one.
    pub(crate) fn edit_file(&mut self, path: PathBuf) {
        let cmd = self.shared.settings.editor.trim().to_string();
        if cmd.is_empty() {
            self.windows.push(Box::new(editor::EditorWindow::new(path)));
            return;
        }
        let cwd = path
            .parent()
            .map(Path::to_path_buf)
            .unwrap_or_else(cfs::home_dir);
        if let Err(e) = launcher::launch(&cmd, &[path], LaunchMode::OncePerFile, &cwd) {
            self.error(e);
        }
    }

    fn copy_move(&mut self, kind: OpKind) {
        let loc = self.panels[self.active].tab().location.clone();
        let entries = self.targets();
        if entries.is_empty() {
            return self.error(tr("dlg.no_selection"));
        }
        let dest = self.other_dir().unwrap_or_default();
        if let Location::Archive { file, .. } = loc {
            if kind == OpKind::Move {
                return self.error(t(
                    "Moving out of an archive is not supported: copy (F5), then delete the archive.",
                    "Impossible de déplacer hors d'une archive : copiez (F5), puis supprimez l'archive.",
                ));
            }
            let inner = entries
                .iter()
                .map(|e| e.path.to_string_lossy().replace('\\', "/"))
                .collect();
            self.dialog = Some(Dialog::input(
                tr("cmd.unpack"),
                tr("dlg.unpack"),
                dest.display().to_string(),
                InputAction::Extract {
                    archive: file,
                    entries: Some(inner),
                },
            ));
            return;
        }
        if matches!(
            self.panels[1 - self.active].tab().location,
            Location::Archive { .. }
        ) {
            return self.error(t(
                "To add files to an archive, use Pack (Alt+F5).",
                "Pour ajouter des fichiers à une archive, utilisez Compresser (Alt+F5).",
            ));
        }
        let sources: Vec<PathBuf> = entries.iter().map(|e| e.path.clone()).collect();
        let label = format!(
            "{} {}",
            match sources.as_slice() {
                [one] => one
                    .file_name()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .into_owned(),
                many => format!("{} {}", many.len(), tr("files")),
            },
            if kind == OpKind::Copy {
                tr("dlg.copy_to")
            } else {
                tr("dlg.move_to")
            }
        );
        let mut text = dest.display().to_string();
        if !text.is_empty() && !text.ends_with(std::path::MAIN_SEPARATOR) {
            text.push(std::path::MAIN_SEPARATOR);
        }
        let title = if kind == OpKind::Copy {
            tr("cmd.copy")
        } else {
            tr("cmd.move")
        };
        self.dialog = Some(Dialog::input(
            title,
            label,
            text,
            InputAction::CopyMove { kind, sources },
        ));
    }

    fn rename_current(&mut self) {
        let tab = self.panels[self.active].tab();
        if !matches!(tab.location, Location::Local(_) | Location::List { .. }) {
            return;
        }
        if let Some(e) = tab.current().filter(|e| !e.is_parent).cloned() {
            self.dialog = Some(Dialog::input(
                tr("cmd.rename"),
                tr("dlg.rename"),
                e.name.clone(),
                InputAction::Rename { path: e.path },
            ));
        }
    }

    fn delete(&mut self, permanent: bool, ctx: &egui::Context) {
        let Some(paths) = self.local_targets() else {
            return;
        };
        let to_trash = self.shared.settings.use_trash && !permanent;
        let what = match paths.as_slice() {
            [one] => one.display().to_string(),
            many => format!("{} {}", many.len(), tr("files")),
        };
        let question = if to_trash {
            tr("dlg.delete")
        } else {
            tr("dlg.delete_perm")
        };
        let dialog = Dialog::Confirm {
            title: tr("cmd.delete").into(),
            text: format!("{question}\n{what}"),
            action: ConfirmAction::Delete { paths, to_trash },
        };
        if self.shared.settings.confirm_delete {
            self.dialog = Some(dialog);
        } else {
            self.run_dialog(dialog, ctx);
        }
    }

    fn properties(&mut self) {
        let Some(e) = self.panels[self.active]
            .tab()
            .current()
            .filter(|e| !e.is_parent)
            .cloned()
        else {
            return;
        };
        let size = match self.panels[self.active].tab().dir_sizes.get(&e.path) {
            Some(s) => *s,
            None => e.size,
        };
        let mut text = format!(
            "{}: {}\n{}: {} ({} B)\n{}: {}\n{}: {}",
            tr("path"),
            e.path.display(),
            tr("size"),
            cfs::human_size(size),
            cfs::grouped(size),
            tr("date"),
            cfs::format_time(e.modified),
            tr("attr"),
            e.attrs,
        );
        if e.is_link
            && let Ok(target) = std::fs::read_link(&e.path)
        {
            text.push_str(&format!("\n→ {}", target.display()));
        }
        if e.is_dir {
            text.push_str(&format!(
                "\n\n{}",
                t(
                    "Space or Ctrl+L computes a folder's size.",
                    "Espace ou Ctrl+L calcule la taille d'un dossier."
                )
            ));
        }
        self.dialog = Some(Dialog::message(&e.name, text));
    }

    fn compare_files(&mut self) {
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
    fn compare_panels(&mut self) {
        use std::collections::HashMap;
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

    fn open_terminal(&mut self) {
        let dir = self.active_dir().unwrap_or_else(cfs::home_dir);
        let custom = self.shared.settings.terminal.trim().to_string();
        let result = if custom.is_empty() {
            default_terminal(&dir)
        } else {
            launcher::run_in_shell(&custom, &dir)
        };
        if let Err(e) = result {
            self.error(e);
        }
    }

    pub(crate) fn start_file_op(&mut self, req: OpRequest, ctx: &egui::Context) {
        let title = match req.kind {
            OpKind::Copy => tr("copying"),
            OpKind::Move => tr("moving"),
            OpKind::Delete { .. } => tr("deleting"),
        };
        self.jobs.start_file_op(title.into(), req, ctx);
    }
}

#[derive(Clone, Copy)]
enum Nav {
    Move(isize),
    Enter,
    Insert,
    Space,
    Escape,
}

fn toggle_cursor(tab: &mut super::panel::Tab) {
    if let Some(e) = tab
        .current()
        .filter(|e| !e.is_parent)
        .map(|e| e.path.clone())
    {
        tab.toggle_selection(&e);
    }
}

/// Opens the platform's terminal in `dir`.
fn default_terminal(dir: &Path) -> Result<(), String> {
    use std::process::Command as Proc;
    let spawn = |cmd: &mut Proc| {
        cmd.current_dir(dir)
            .spawn()
            .map(|_| ())
            .map_err(|e| e.to_string())
    };
    if cfg!(windows) {
        spawn(Proc::new("cmd").args(["/c", "start", "cmd"]))
    } else if cfg!(target_os = "macos") {
        spawn(Proc::new("open").args(["-a", "Terminal"]).arg(dir))
    } else {
        let candidates = [
            "x-terminal-emulator",
            "gnome-terminal",
            "konsole",
            "xfce4-terminal",
            "alacritty",
            "kitty",
            "xterm",
        ];
        for c in candidates {
            if spawn(&mut Proc::new(c)).is_ok() {
                return Ok(());
            }
        }
        Err(t(
            "No terminal found: set one in the settings.",
            "Aucun terminal trouvé : choisissez-en un dans la configuration.",
        )
        .into())
    }
}

/// Default checksum output: `<name>.<ext>` for one file, `CHECKSUM.<ext>` for several.
pub(crate) fn checksum_out(
    files: &[PathBuf],
    base: &Path,
    algo: Algo,
    once_per_file: bool,
) -> String {
    if once_per_file {
        return String::new();
    }
    let name = match files {
        [one] => format!(
            "{}.{}",
            one.file_name().unwrap_or_default().to_string_lossy(),
            algo.extension()
        ),
        _ => format!("CHECKSUM.{}", algo.extension()),
    };
    base.join(name).display().to_string()
}
