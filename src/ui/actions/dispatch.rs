//! `App::run`: what each `Command` does. The top-level match stays exhaustive and hands each
//! command to the dispatcher of its group; a group's `_` arm is never reached.

use std::path::PathBuf;

use eframe::egui;

use crate::core::fileops::OpKind;
use crate::core::fs::{self as cfs, SortKey};
use crate::i18n::{t, tr};
use crate::ui::dialogs::{Dialog, InputAction, OpenWithForm, TimeMode};
use crate::ui::keys::Command;
use crate::ui::panel::{Location, ViewMode};
use crate::ui::{App, ftp_view, lister, rename_view, search_view, settings_view, sync_view};

impl App {
    pub(crate) fn run(&mut self, cmd: Command, ctx: &egui::Context) {
        use Command::*;
        match cmd {
            View | Edit | EditNew | Copy | Move | RenameInPlace | MkDir | Delete
            | DeletePermanent | Properties | OpenWith | Quit => self.run_file(cmd, ctx),
            Pack | Unpack | BackupZip | Split | Combine | ChecksumCreate | ChecksumVerify
            | AddTime | ReDate | Touch | Catalog | Encrypt | Decrypt | NetConnect
            | NetDisconnect | BrowseFolder => self.run_tool(cmd, ctx),
            SelectGroup | UnselectGroup | SelectAll | UnselectAll | Invert | SelectSameExt
            | CopyNames | CopyPaths | ComparePanels | DirSizes => self.run_select(cmd, ctx),
            Search | MultiRename | CompareFiles | Sync | Ftp | Passwords | Terminal | Settings
            | Log | About | Keys => self.run_window(cmd),
            SettingsFolder => self.ask_settings_dir(false),
            CheckUpdate => self
                .windows
                .push(Box::new(crate::ui::update_view::UpdateWindow::new(ctx))),
            QuickView | ViewFull | ViewBrief | ViewThumbs | ToggleHidden | Branch | QuickFilter
            | Refresh | SortName | SortExt | SortDate | SortSize => self.run_view(cmd),
            Swap | SameDir | NewTab | CloseTab | NextTab | Hotlist | AddHotlist | History
            | Back | Forward | Parent | Root | Home | SwitchPanel | DrivesLeft | DrivesRight => {
                self.run_nav(cmd)
            }
        }
    }

    fn run_file(&mut self, cmd: Command, ctx: &egui::Context) {
        use Command::*;
        match cmd {
            View => self.view_current(),
            Edit => self.edit_current(),
            EditNew => self.ask_in_dir("cmd.edit_new", "dlg.newfile", |dir| InputAction::NewFile {
                dir,
            }),
            Copy => self.copy_move(OpKind::Copy),
            Move => self.copy_move(OpKind::Move),
            RenameInPlace => self.rename_current(),
            MkDir => self.ask_in_dir("cmd.mkdir", "dlg.mkdir", |dir| InputAction::MkDir { dir }),
            Delete => self.delete(false, ctx),
            DeletePermanent => self.delete(true, ctx),
            Properties => self.properties(),
            OpenWith => {
                if let Some(files) = self.local_targets() {
                    self.dialog = Some(Dialog::OpenWith(OpenWithForm {
                        files,
                        command: String::new(),
                        once_per_file: false,
                    }));
                }
            }
            Quit => ctx.send_viewport_cmd(egui::ViewportCommand::Close),
            _ => misrouted(cmd),
        }
    }

    fn run_tool(&mut self, cmd: Command, ctx: &egui::Context) {
        use Command::*;
        match cmd {
            Pack => self.pack_dialog(),
            Unpack => self.unpack_dialog(),
            BackupZip => self.backup_zip(ctx),
            Split => self.split_dialog(),
            Combine => self.combine_dialog(),
            ChecksumCreate => self.checksum_dialog(),
            ChecksumVerify => self.checksum_verify(ctx),
            AddTime => self.time_dialog(TimeMode::Shift),
            ReDate => self.time_dialog(TimeMode::Sequence),
            Touch => self.time_dialog(TimeMode::Fixed),
            Catalog => self.catalog_dialog(),
            Encrypt => self.crypt_dialog(true),
            Decrypt => self.crypt_dialog(false),
            NetConnect => self.net_connect_dialog(),
            NetDisconnect => self.net_disconnect(ctx),
            BrowseFolder => {
                let start = self.active_dir();
                self.folder_pick.start(ctx, start.as_deref());
            }
            _ => misrouted(cmd),
        }
    }

    fn run_select(&mut self, cmd: Command, ctx: &egui::Context) {
        use Command::*;
        match cmd {
            SelectGroup | UnselectGroup => self.select_group_dialog(cmd == SelectGroup),
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
                    .map(|e| match cmd {
                        CopyNames => e.name.clone(),
                        _ => e.path.display().to_string(),
                    })
                    .collect();
                ctx.copy_text(text.join("\n"));
            }
            ComparePanels => self.compare_panels(),
            DirSizes => self.visible_dir_sizes(),
            _ => misrouted(cmd),
        }
    }

    fn run_window(&mut self, cmd: Command) {
        use Command::*;
        match cmd {
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
            Sync => self.open_sync(),
            Ftp => self.windows.push(Box::new(ftp_view::FtpWindow::new())),
            Passwords => self
                .windows
                .push(Box::new(settings_view::PasswordsWindow::new())),
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
            _ => misrouted(cmd),
        }
    }

    fn run_view(&mut self, cmd: Command) {
        use Command::*;
        let hidden = self.show_hidden();
        match cmd {
            QuickView => {
                self.quick_view = match self.quick_view {
                    Some(_) => None,
                    None => Some(crate::ui::QuickView {
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
            SortName => self.active_panel().tab_mut().set_sort(SortKey::Name),
            SortExt => self.active_panel().tab_mut().set_sort(SortKey::Ext),
            SortDate => self.active_panel().tab_mut().set_sort(SortKey::Modified),
            SortSize => self.active_panel().tab_mut().set_sort(SortKey::Size),
            _ => misrouted(cmd),
        }
    }

    fn run_nav(&mut self, cmd: Command) {
        use Command::*;
        let hidden = self.show_hidden();
        match cmd {
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
            Parent => self.go_parent(),
            Root => self.go_root(),
            Home => {
                self.active_panel()
                    .tab_mut()
                    .go(Location::Local(cfs::home_dir()), hidden);
            }
            SwitchPanel => self.active = 1 - self.active,
            DrivesLeft => self.dialog = Some(Dialog::Drives { side: 0 }),
            DrivesRight => self.dialog = Some(Dialog::Drives { side: 1 }),
            _ => misrouted(cmd),
        }
    }

    /// An input dialog for a new name in the active folder (F7, Shift+F4).
    fn ask_in_dir(
        &mut self,
        title: &'static str,
        label: &'static str,
        action: fn(PathBuf) -> InputAction,
    ) {
        if let Some(dir) = self.active_dir() {
            self.dialog = Some(Dialog::input(tr(title), tr(label), "", action(dir)));
        }
    }

    fn select_group_dialog(&mut self, select: bool) {
        let (title, label) = if select {
            (tr("cmd.select_group"), tr("dlg.select"))
        } else {
            (tr("cmd.unselect_group"), tr("dlg.unselect"))
        };
        self.dialog = Some(Dialog::input(
            title,
            label,
            "*.*",
            InputAction::Select { select },
        ));
    }

    fn visible_dir_sizes(&mut self) {
        let tab = self.panels[self.active].tab();
        if !matches!(tab.location, Location::Local(_)) {
            return;
        }
        let dirs: Vec<PathBuf> = tab
            .visible_entries()
            .filter(|e| e.is_dir && !e.is_parent)
            .map(|e| e.path.clone())
            .collect();
        self.compute_dir_sizes(dirs);
    }

    fn open_sync(&mut self) {
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
}

/// A group dispatcher got a command `run` routes elsewhere: it would silently do nothing.
fn misrouted(cmd: Command) {
    debug_assert!(false, "{cmd:?} reached the wrong command group");
}
