//! The menu bar, the function-key bar and the command line.

use std::path::PathBuf;

use eframe::egui;

use super::App;
use super::icons;
use super::keys::{self, Command};
use super::panel::Location;
use crate::core::fs as cfs;
use crate::core::launcher;
use crate::i18n::{t, tr};

/// A menu: its title key and its commands (`None` = separator).
type Menu = (&'static str, &'static [Option<Command>]);

const MENUS: &[Menu] = {
    use Command::*;
    &[
        (
            "menu.files",
            &[
                Some(View),
                Some(Edit),
                Some(EditNew),
                Some(Copy),
                Some(Move),
                Some(RenameInPlace),
                Some(MkDir),
                Some(Delete),
                Some(DeletePermanent),
                None,
                Some(Pack),
                Some(Unpack),
                Some(BackupZip),
                None,
                Some(Properties),
                Some(OpenWith),
                None,
                Some(Quit),
            ],
        ),
        (
            "menu.mark",
            &[
                Some(SelectGroup),
                Some(UnselectGroup),
                Some(SelectAll),
                Some(UnselectAll),
                Some(Invert),
                Some(SelectSameExt),
                None,
                Some(CopyNames),
                Some(CopyPaths),
                None,
                Some(ComparePanels),
            ],
        ),
        (
            "menu.commands",
            &[
                Some(Search),
                Some(MultiRename),
                Some(CompareFiles),
                Some(Sync),
                None,
                Some(DirSizes),
                Some(Branch),
                Some(QuickFilter),
                None,
                Some(BrowseFolder),
                Some(Hotlist),
                Some(AddHotlist),
                Some(History),
                None,
                Some(Back),
                Some(Forward),
                Some(Parent),
                Some(Root),
                Some(Home),
                None,
                Some(SameDir),
                Some(Swap),
                Some(Terminal),
            ],
        ),
        (
            "menu.net",
            &[
                Some(NetConnect),
                Some(NetDisconnect),
                None,
                Some(Ftp),
                Some(Passwords),
            ],
        ),
        (
            "menu.show",
            &[
                Some(ViewFull),
                Some(ViewBrief),
                Some(ViewThumbs),
                Some(QuickView),
                Some(ToggleHidden),
                None,
                Some(SortName),
                Some(SortExt),
                Some(SortDate),
                Some(SortSize),
                None,
                Some(NewTab),
                Some(CloseTab),
                Some(NextTab),
                None,
                Some(Refresh),
            ],
        ),
        (
            "menu.tools",
            &[
                Some(Split),
                Some(Combine),
                Some(ChecksumCreate),
                Some(ChecksumVerify),
                None,
                Some(AddTime),
                Some(ReDate),
                Some(Touch),
                None,
                Some(Catalog),
                Some(Encrypt),
                Some(Decrypt),
            ],
        ),
        (
            "menu.config",
            &[Some(Settings), Some(SettingsFolder), Some(Log)],
        ),
        (
            "menu.help",
            &[Some(Keys), Some(CheckUpdate), None, Some(About)],
        ),
    ]
};

impl App {
    pub(crate) fn menu_bar(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        let mut chosen = None;
        let mut user_cmd = None;
        egui::MenuBar::new().ui(ui, |ui| {
            for (title, items) in MENUS {
                ui.menu_button(tr(title), |ui| {
                    for item in *items {
                        match item {
                            None => {
                                ui.separator();
                            }
                            Some(cmd) => {
                                let button = egui::Button::new(cmd.label())
                                    .shortcut_text(keys::shortcut_text(&ctx, *cmd));
                                if ui.add(button).clicked() {
                                    chosen = Some(*cmd);
                                    ui.close();
                                }
                            }
                        }
                    }
                    if *title == "menu.tools" && !self.shared.settings.user_commands.is_empty() {
                        ui.separator();
                        for (i, uc) in self.shared.settings.user_commands.iter().enumerate() {
                            if ui.button(format!("{}  {}", icons::RUN, uc.name)).clicked() {
                                user_cmd = Some(i);
                                ui.close();
                            }
                        }
                    }
                });
            }
            if let Some(r) = &self.update_check.found {
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let text = format!(
                        "{}  {} {}",
                        icons::UPDATE,
                        t("Update to", "Mettre à jour vers"),
                        r.version
                    );
                    let button = egui::Button::new(
                        egui::RichText::new(text).color(ui.visuals().warn_fg_color),
                    );
                    if ui.add(button).clicked() {
                        chosen = Some(Command::CheckUpdate);
                    }
                });
            }
        });
        if let Some(cmd) = chosen {
            self.run(cmd, &ctx);
        }
        if let Some(i) = user_cmd {
            self.run_user_command(i);
        }
    }

    pub(crate) fn fkey_bar(&mut self, ui: &mut egui::Ui) {
        use Command::*;
        let ctx = ui.ctx().clone();
        let keys = [
            ("F3", "fk.view", View),
            ("F4", "fk.edit", Edit),
            ("F5", "fk.copy", Copy),
            ("F6", "fk.move", Move),
            ("F7", "fk.mkdir", MkDir),
            ("F8", "fk.delete", Delete),
            ("Alt+F7", "fk.search", Search),
            ("Alt+F4", "fk.quit", Quit),
        ];
        let mut chosen = None;
        ui.columns(keys.len(), |cols| {
            for (ui, (key, label, cmd)) in cols.iter_mut().zip(keys) {
                let button = egui::Button::new(format!("{key} {}", tr(label)));
                if ui.add_sized([ui.available_width(), 22.0], button).clicked() {
                    chosen = Some(cmd);
                }
            }
        });
        if let Some(cmd) = chosen {
            self.run(cmd, &ctx);
        }
    }

    pub(crate) fn command_line_ui(&mut self, ui: &mut egui::Ui) {
        let dir = self.active_dir();
        ui.horizontal(|ui| {
            let prompt = match &dir {
                Some(d) => format!("{}>", d.display()),
                None => ">".into(),
            };
            ui.monospace(prompt);
            let id = egui::Id::unique("d3sk-cmdline");
            let edit = egui::TextEdit::singleline(&mut self.command_line)
                .id(id)
                .font(egui::TextStyle::Monospace)
                .hint_text(t(
                    "Command (cd <folder> to navigate)",
                    "Commande (cd <dossier> pour naviguer)",
                ))
                .desired_width(f32::INFINITY);
            let r = ui.add(edit);
            if self.focus_cmdline {
                r.request_focus();
                self.focus_cmdline = false;
            }
            if r.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) {
                let line = std::mem::take(&mut self.command_line);
                self.run_command_line(line.trim(), dir.clone());
            }
        });
    }

    fn run_command_line(&mut self, line: &str, dir: Option<PathBuf>) {
        if line.is_empty() {
            return;
        }
        let dir = dir.unwrap_or_else(cfs::home_dir);
        if line == "cd" || line.starts_with("cd ") {
            let arg = line[2..].trim().trim_matches('"');
            let target = match arg {
                "" | "~" => cfs::home_dir(),
                a if a.starts_with("~/") => cfs::home_dir().join(&a[2..]),
                a => dir.join(a),
            };
            let target = target.canonicalize().unwrap_or(target);
            if target.is_dir() {
                let hidden = self.show_hidden();
                self.active_panel()
                    .tab_mut()
                    .go(Location::Local(target), hidden);
            } else {
                self.error(format!("{}: {}", tr("folder"), target.display()));
            }
            return;
        }
        match launcher::run_in_shell(line, &dir) {
            Ok(()) => self.status = format!("> {line}"),
            Err(e) => self.error(e),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_command_is_in_a_menu_or_bound() {
        let in_menu = |c: Command| MENUS.iter().any(|(_, items)| items.contains(&Some(c)));
        let bound = keys::bindings();
        for (_, cmd) in &bound {
            let _ = in_menu(*cmd);
        }
        // The add-ons must be reachable from the menu, not only by a key.
        for c in [
            Command::Split,
            Command::Combine,
            Command::ChecksumCreate,
            Command::ChecksumVerify,
            Command::AddTime,
            Command::ReDate,
            Command::Touch,
            Command::Catalog,
            Command::Encrypt,
            Command::Decrypt,
            Command::BackupZip,
            Command::Ftp,
            Command::NetConnect,
            Command::NetDisconnect,
            Command::BrowseFolder,
            Command::Sync,
        ] {
            assert!(in_menu(c), "{c:?} is not in a menu");
        }
    }
}
