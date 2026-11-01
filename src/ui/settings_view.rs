//! Settings window (tabs, OK / Apply / Cancel) and the password manager.

mod passwords;

use eframe::egui;

use super::{AppEvent, Shared, ToolWindow};
use crate::config::{self, HotlistItem, Settings, UserCommand};
use crate::i18n::{Lang, t, tr};
pub use passwords::PasswordsWindow;

#[derive(PartialEq, Eq, Clone, Copy)]
enum Tab {
    General,
    Columns,
    Programs,
    Hotlist,
    Commands,
}

/// One hotlist row while editing: the path is text until applied.
struct HotRow {
    name: String,
    path: String,
}

pub struct SettingsWindow {
    id: egui::Id,
    tab: Tab,
    draft: Settings,
    hot: Vec<HotRow>,
}

/// What the buttons at the bottom asked for.
#[derive(PartialEq, Eq)]
enum Choice {
    None,
    Ok,
    Apply,
    Cancel,
}

impl SettingsWindow {
    pub fn new(settings: &Settings) -> Self {
        let hot = settings
            .hotlist
            .iter()
            .map(|h| HotRow {
                name: h.name.clone(),
                path: h.path.to_string_lossy().into_owned(),
            })
            .collect();
        Self {
            id: super::window_id("settings"),
            tab: Tab::General,
            draft: settings.clone(),
            hot,
        }
    }

    /// The draft with the hotlist rows folded back in (rows without a path are dropped).
    fn collect(&self) -> Settings {
        let mut s = self.draft.clone();
        s.hotlist = self
            .hot
            .iter()
            .filter(|r| !r.path.trim().is_empty())
            .map(|r| HotlistItem {
                name: r.name.clone(),
                path: r.path.trim().into(),
            })
            .collect();
        s
    }

    fn apply(&self, shared: &mut Shared) {
        apply_edits(&self.collect(), &mut shared.settings);
        shared.send(AppEvent::SettingsChanged);
    }

    fn tabs_ui(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            let tabs = [
                (Tab::General, t("General", "Général")),
                (Tab::Columns, tr("columns")),
                (Tab::Programs, t("Programs", "Programmes")),
                (Tab::Hotlist, tr("cmd.hotlist")),
                (Tab::Commands, t("User commands", "Commandes utilisateur")),
            ];
            for (tab, label) in tabs {
                ui.selectable_value(&mut self.tab, tab, label);
            }
        });
    }

    fn general_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        let d = &mut self.draft;
        ui.horizontal(|ui| {
            ui.label(tr("language"));
            egui::ComboBox::from_id_salt("settings-lang")
                .selected_text(d.lang.label())
                .show_ui(ui, |ui| {
                    for &l in Lang::all() {
                        ui.selectable_value(&mut d.lang, l, l.label());
                    }
                });
        });
        ui.checkbox(&mut d.dark_mode, tr("dark_mode"));
        ui.horizontal(|ui| {
            ui.label(tr("font_size"));
            ui.add(egui::Slider::new(&mut d.font_size, 10.0..=24.0).step_by(1.0));
        });
        ui.checkbox(&mut d.show_hidden, tr("cmd.hidden"));
        ui.checkbox(&mut d.use_trash, tr("use_trash"));
        ui.checkbox(&mut d.confirm_delete, tr("confirm_delete"));
        ui.checkbox(&mut d.log_operations, tr("log_ops"));
        if ui.button(tr("cmd.log")).clicked() {
            shared.send(AppEvent::View(config::log_path()));
        }
        ui.separator();
        ui.label(format!(
            "{}: {}",
            t("Settings file", "Fichier de réglages"),
            config::settings_path().display()
        ));
        ui.weak(t(
            "Put d3skcommander.json next to the executable to turn on portable mode.",
            "Placez d3skcommander.json à côté de l'exécutable pour activer le mode portable.",
        ));
    }

    fn columns_ui(&mut self, ui: &mut egui::Ui) {
        let c = &mut self.draft.columns;
        ui.checkbox(&mut c.ext, tr("ext"));
        ui.checkbox(&mut c.size, tr("size"));
        ui.checkbox(&mut c.date, tr("date"));
        ui.checkbox(&mut c.attrs, tr("attr"));
    }

    fn programs_ui(&mut self, ui: &mut egui::Ui) {
        let d = &mut self.draft;
        egui::Grid::new("settings-programs")
            .num_columns(2)
            .show(ui, |ui| {
                ui.label(tr("editor"));
                ui.add(egui::TextEdit::singleline(&mut d.editor).desired_width(320.0));
                ui.end_row();
                ui.label(tr("terminal"));
                ui.add(egui::TextEdit::singleline(&mut d.terminal).desired_width(320.0));
                ui.end_row();
            });
        ui.weak(t(
            "Empty means the built-in editor / the platform terminal.",
            "Vide = éditeur intégré / terminal de la plateforme.",
        ));
    }

    fn hotlist_ui(&mut self, ui: &mut egui::Ui) {
        let mut remove = None;
        let mut swap = None;
        let last = self.hot.len().saturating_sub(1);
        egui::ScrollArea::vertical()
            .id_salt("settings-hotlist")
            .max_height(260.0)
            .show(ui, |ui| {
                for (i, row) in self.hot.iter_mut().enumerate() {
                    ui.horizontal(|ui| {
                        ui.add(egui::TextEdit::singleline(&mut row.name).desired_width(110.0));
                        ui.add(egui::TextEdit::singleline(&mut row.path).desired_width(250.0));
                        if ui.add_enabled(i > 0, egui::Button::new("↑")).clicked() {
                            swap = Some((i - 1, i));
                        }
                        if ui.add_enabled(i < last, egui::Button::new("↓")).clicked() {
                            swap = Some((i, i + 1));
                        }
                        if ui.button(t("Remove", "Retirer")).clicked() {
                            remove = Some(i);
                        }
                    });
                }
            });
        if let Some((a, b)) = swap {
            self.hot.swap(a, b);
        }
        if let Some(i) = remove {
            self.hot.remove(i);
        }
        if ui.button(t("Add", "Ajouter")).clicked() {
            self.hot.push(HotRow {
                name: String::new(),
                path: String::new(),
            });
        }
    }

    fn commands_ui(&mut self, ui: &mut egui::Ui) {
        ui.weak(t(
            "%s = the selected files",
            "%s = les fichiers sélectionnés",
        ));
        let mut remove = None;
        egui::ScrollArea::vertical()
            .id_salt("settings-commands")
            .max_height(260.0)
            .show(ui, |ui| {
                for (i, c) in self.draft.user_commands.iter_mut().enumerate() {
                    ui.horizontal(|ui| {
                        ui.add(egui::TextEdit::singleline(&mut c.name).desired_width(100.0));
                        ui.add(egui::TextEdit::singleline(&mut c.command).desired_width(240.0));
                        ui.checkbox(&mut c.once_per_file, tr("dlg.once_per_file"));
                        if ui.button(t("Remove", "Retirer")).clicked() {
                            remove = Some(i);
                        }
                    });
                }
            });
        if let Some(i) = remove {
            self.draft.user_commands.remove(i);
        }
        if ui.button(t("Add", "Ajouter")).clicked() {
            self.draft.user_commands.push(UserCommand {
                name: String::new(),
                command: String::new(),
                once_per_file: false,
            });
        }
    }

    fn buttons_ui(&self, ui: &mut egui::Ui) -> Choice {
        let mut choice = Choice::None;
        ui.horizontal(|ui| {
            if ui.button(tr("ok")).clicked() {
                choice = Choice::Ok;
            }
            if ui.button(tr("apply")).clicked() {
                choice = Choice::Apply;
            }
            if ui.button(tr("cancel")).clicked() {
                choice = Choice::Cancel;
            }
        });
        choice
    }
}

impl ToolWindow for SettingsWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        let mut open = true;
        let mut choice = Choice::None;
        super::tool_window(tr("cmd.settings"), ctx, [560.0, 420.0])
            .id(self.id)
            .open(&mut open)
            .collapsible(false)
            .show(ctx, |ui| {
                self.tabs_ui(ui);
                ui.separator();
                match self.tab {
                    Tab::General => self.general_ui(ui, shared),
                    Tab::Columns => self.columns_ui(ui),
                    Tab::Programs => self.programs_ui(ui),
                    Tab::Hotlist => self.hotlist_ui(ui),
                    Tab::Commands => self.commands_ui(ui),
                }
                ui.separator();
                choice = self.buttons_ui(ui);
            });
        match choice {
            Choice::Ok => {
                self.apply(shared);
                return false;
            }
            Choice::Apply => self.apply(shared),
            Choice::Cancel => return false,
            Choice::None => {}
        }
        open
    }
}

/// Copies the fields this window edits; panels, history and FTP sites stay as they are.
fn apply_edits(from: &Settings, to: &mut Settings) {
    to.lang = from.lang;
    to.dark_mode = from.dark_mode;
    to.font_size = from.font_size;
    to.show_hidden = from.show_hidden;
    to.use_trash = from.use_trash;
    to.confirm_delete = from.confirm_delete;
    to.log_operations = from.log_operations;
    to.editor = from.editor.clone();
    to.terminal = from.terminal.clone();
    to.columns = from.columns.clone();
    to.hotlist = from.hotlist.clone();
    to.user_commands = from.user_commands.clone();
}

#[cfg(test)]
mod tests {
    use std::path::PathBuf;

    use super::*;
    use crate::core::ftp::FtpSite;

    #[test]
    fn apply_keeps_what_the_window_does_not_edit() {
        let mut live = Settings::default();
        live.history.push(PathBuf::from("/h"));
        live.left.tabs.push(PathBuf::from("/l"));
        live.ftp_sites.push(FtpSite::default());

        let mut win = SettingsWindow::new(&live);
        // The live settings move on while the window is open.
        live.history.push(PathBuf::from("/h2"));
        live.ftp_sites.push(FtpSite::default());
        win.draft.dark_mode = !live.dark_mode;
        win.draft.editor = "vim".into();
        win.hot.push(HotRow {
            name: "tmp".into(),
            path: " /tmp ".into(),
        });
        win.hot.push(HotRow {
            name: "empty".into(),
            path: "  ".into(),
        });

        apply_edits(&win.collect(), &mut live);
        assert_eq!(live.editor, "vim");
        assert_eq!(live.dark_mode, win.draft.dark_mode);
        assert_eq!(live.hotlist.len(), 1);
        assert_eq!(live.hotlist[0].path, PathBuf::from("/tmp"));
        assert_eq!(live.history.len(), 2);
        assert_eq!(live.ftp_sites.len(), 2);
        assert_eq!(live.left.tabs, [PathBuf::from("/l")]);
    }
}
