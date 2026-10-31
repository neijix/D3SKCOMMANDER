//! The password manager window: unlock (or create) the store, list, add, edit, remove.

use std::collections::HashSet;
use std::io;
use std::sync::mpsc::{Receiver, TryRecvError, channel};

use eframe::egui;

use super::super::{Shared, ToolWindow, window_id};
use crate::config;
use crate::core::crypt::PasswordStore;
use crate::i18n::{t, tr};

type Unlocking = Receiver<io::Result<PasswordStore>>;

pub struct PasswordsWindow {
    id: egui::Id,
    master: String,
    confirm: String,
    /// The background `PasswordStore::open` while it runs.
    unlocking: Option<Unlocking>,
    error: Option<String>,
    key: String,
    secret: String,
    revealed: HashSet<String>,
    /// Changes not yet written with Save.
    dirty: bool,
    status: String,
}

impl PasswordsWindow {
    pub fn new() -> Self {
        Self {
            id: window_id("passwords"),
            master: String::new(),
            confirm: String::new(),
            unlocking: None,
            error: None,
            key: String::new(),
            secret: String::new(),
            revealed: HashSet::new(),
            dirty: false,
            status: String::new(),
        }
    }

    /// Opens (or creates and saves) the store off the UI thread: scrypt takes about a second.
    fn start_unlock(&mut self, ctx: &egui::Context) {
        let master = std::mem::take(&mut self.master);
        self.confirm.clear();
        self.error = None;
        let (tx, rx) = channel();
        let ctx = ctx.clone();
        let path = config::passwords_path();
        let spawned = std::thread::Builder::new()
            .name("passwords-unlock".into())
            .spawn(move || {
                let creating = !path.exists();
                let result = PasswordStore::open(&path, &master).and_then(|store| {
                    if creating {
                        store.save()?;
                    }
                    Ok(store)
                });
                let _ = tx.send(result);
                ctx.request_repaint();
            });
        match spawned {
            Ok(_) => self.unlocking = Some(rx),
            Err(e) => self.error = Some(e.to_string()),
        }
    }

    fn poll_unlock(&mut self, shared: &mut Shared) {
        let Some(rx) = &self.unlocking else {
            return;
        };
        match rx.try_recv() {
            Ok(Ok(store)) => {
                shared.passwords = Some(store);
                self.unlocking = None;
                self.status.clear();
            }
            Ok(Err(e)) => {
                self.unlocking = None;
                self.error = Some(if e.kind() == io::ErrorKind::PermissionDenied {
                    t("Wrong master password", "Mot de passe maître incorrect").to_string()
                } else {
                    e.to_string()
                });
            }
            Err(TryRecvError::Empty) => {}
            Err(TryRecvError::Disconnected) => {
                self.unlocking = None;
                self.error = Some(t("Unlock failed", "Échec du déverrouillage").to_string());
            }
        }
    }

    fn locked_ui(&mut self, ui: &mut egui::Ui) {
        let creating = !config::passwords_path().exists();
        if self.unlocking.is_some() {
            ui.horizontal(|ui| {
                ui.spinner();
                ui.label(t("Unlocking…", "Déverrouillage…"));
            });
            return;
        }
        if creating {
            ui.label(t(
                "No password store yet: choose a master password to create one.",
                "Pas encore de coffre : choisissez un mot de passe maître pour le créer.",
            ));
        }
        ui.label(tr("master_password"));
        let first = ui.add(egui::TextEdit::singleline(&mut self.master).password(true));
        let mut enter = first.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter));
        if creating {
            ui.label(tr("confirm_password"));
            let second = ui.add(egui::TextEdit::singleline(&mut self.confirm).password(true));
            enter |= second.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter));
        }
        let mismatch = creating && self.master != self.confirm;
        if mismatch && !self.confirm.is_empty() {
            ui.colored_label(
                ui.visuals().warn_fg_color,
                t("The passwords do not match", "Les mots de passe diffèrent"),
            );
        }
        if let Some(e) = &self.error {
            ui.colored_label(ui.visuals().error_fg_color, e);
        }
        let ready = !self.master.is_empty() && !mismatch;
        let clicked = ui
            .add_enabled(ready, egui::Button::new(t("Unlock", "Déverrouiller")))
            .clicked();
        if ready && (clicked || enter) {
            self.start_unlock(ui.ctx());
        }
    }

    fn unlocked_ui(&mut self, ui: &mut egui::Ui, shared: &mut Shared) {
        let Some(store) = shared.passwords.as_mut() else {
            return;
        };
        let mut remove = None;
        let mut edit = None;
        egui::ScrollArea::vertical()
            .id_salt("passwords-list")
            .max_height(240.0)
            .auto_shrink([false, true])
            .show(ui, |ui| {
                for key in store.keys() {
                    ui.horizontal(|ui| {
                        ui.label(&key);
                        let shown = self.revealed.contains(&key);
                        let text = match store.get(&key) {
                            Some(s) if shown => s.to_string(),
                            Some(_) => "••••••••".to_string(),
                            None => String::new(),
                        };
                        ui.monospace(text);
                        let mut on = shown;
                        if ui.checkbox(&mut on, t("Show", "Afficher")).changed() {
                            if on {
                                self.revealed.insert(key.clone());
                            } else {
                                self.revealed.remove(&key);
                            }
                        }
                        if ui.button(t("Edit", "Modifier")).clicked() {
                            edit = Some(key.clone());
                        }
                        if ui.button(t("Remove", "Retirer")).clicked() {
                            remove = Some(key.clone());
                        }
                    });
                }
            });
        if let Some(key) = edit {
            self.secret = store.get(&key).unwrap_or_default().to_string();
            self.key = key;
        }
        if let Some(key) = remove {
            store.remove(&key);
            self.revealed.remove(&key);
            self.dirty = true;
        }
        ui.separator();
        ui.horizontal(|ui| {
            ui.add(
                egui::TextEdit::singleline(&mut self.key)
                    .hint_text(t("Name", "Nom"))
                    .desired_width(180.0),
            );
            ui.add(
                egui::TextEdit::singleline(&mut self.secret)
                    .password(true)
                    .hint_text(tr("password"))
                    .desired_width(160.0),
            );
            let ok = !self.key.trim().is_empty();
            if ui
                .add_enabled(ok, egui::Button::new(t("Set", "Définir")))
                .clicked()
            {
                store.set(self.key.trim(), &self.secret);
                self.key.clear();
                self.secret.clear();
                self.dirty = true;
            }
        });
        ui.separator();
        self.footer(ui, shared);
    }

    fn footer(&mut self, ui: &mut egui::Ui, shared: &mut Shared) {
        let mut lock = false;
        ui.horizontal(|ui| {
            if ui
                .add_enabled(self.dirty, egui::Button::new(tr("save")))
                .clicked()
                && let Some(store) = &shared.passwords
            {
                match store.save() {
                    Ok(()) => {
                        self.dirty = false;
                        self.status = t("Saved.", "Enregistré.").to_string();
                    }
                    Err(e) => self.status = format!("{}: {e}", tr("error")),
                }
            }
            if ui.button(t("Lock", "Verrouiller")).clicked() {
                lock = true;
            }
            if self.dirty {
                ui.weak(t("unsaved changes", "modifications non enregistrées"));
            } else if !self.status.is_empty() {
                ui.label(&self.status);
            }
        });
        if lock {
            shared.passwords = None;
            self.revealed.clear();
            self.key.clear();
            self.secret.clear();
            self.dirty = false;
            self.status.clear();
        }
    }
}

impl Default for PasswordsWindow {
    fn default() -> Self {
        Self::new()
    }
}

impl ToolWindow for PasswordsWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        self.poll_unlock(shared);
        let mut open = true;
        egui::Window::new(tr("cmd.passwords"))
            .id(self.id)
            .open(&mut open)
            .collapsible(false)
            .default_width(460.0)
            .show(ctx, |ui| {
                if shared.passwords.is_some() {
                    self.unlocked_ui(ui, shared);
                } else {
                    self.locked_ui(ui);
                }
            });
        open
    }
}
