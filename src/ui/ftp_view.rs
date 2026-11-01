//! FTP / FTPS client: a site manager on the left, the remote folder and a log on the right.
//! All network work happens in [`worker`]; this file only draws and exchanges messages.

mod table;
mod worker;

use std::collections::BTreeSet;
use std::sync::atomic::Ordering;
use std::sync::mpsc::TryRecvError;

use eframe::egui;
use egui_extras::{Size, StripBuilder};

use super::{AppEvent, Shared, ToolWindow};
use crate::core::fs::human_size;
use crate::core::ftp::{FtpSite, RemoteEntry};
use crate::i18n::{t, tr};
use worker::{Cmd, Ev, Handle, Item, join_remote, store_key};

const MAX_LOG_LINES: usize = 500;

#[derive(PartialEq, Eq, Clone, Copy)]
enum State {
    Idle,
    Connecting,
    Connected,
}

enum InputKind {
    NewFolder,
    Rename(String),
    ConfirmDelete(Vec<Item>),
}

/// The inline field (or confirmation) shown above the listing.
struct Input {
    kind: InputKind,
    text: String,
    fresh: bool,
}

enum Action {
    Up,
    Refresh,
    Download,
    Upload,
    NewFolder,
    Rename,
    Delete,
    Disconnect,
    Cancel,
}

pub struct FtpWindow {
    id: egui::Id,
    sel_site: Option<usize>,
    form: FtpSite,
    password: String,
    /// The stored password was already looked up for the current form.
    prefilled: bool,
    link: Option<Handle>,
    state: State,
    /// Commands (and the connect itself) whose `Done` has not arrived yet.
    pending: usize,
    pwd: String,
    entries: Vec<RemoteEntry>,
    selection: BTreeSet<String>,
    progress: Option<(String, u64)>,
    input: Option<Input>,
    log: Vec<String>,
}

impl FtpWindow {
    pub fn new() -> Self {
        Self {
            id: super::window_id("FtpWindow"),
            sel_site: None,
            form: blank_site(),
            password: String::new(),
            prefilled: true,
            link: None,
            state: State::Idle,
            pending: 0,
            pwd: String::new(),
            entries: Vec::new(),
            selection: BTreeSet::new(),
            progress: None,
            input: None,
            log: Vec::new(),
        }
    }

    fn log(&mut self, line: impl Into<String>) {
        self.log.push(line.into());
        if self.log.len() > MAX_LOG_LINES {
            self.log.drain(..self.log.len() - MAX_LOG_LINES);
        }
    }

    fn connected(&self) -> bool {
        self.state == State::Connected
    }

    fn send(&mut self, cmd: Cmd) {
        if let Some(link) = &self.link
            && link.cmd.send(cmd).is_ok()
        {
            self.pending += 1;
        }
    }

    // ---- site manager ---------------------------------------------------------------

    fn select_site(&mut self, i: usize, shared: &Shared) {
        if let Some(site) = shared.settings.ftp_sites.get(i) {
            self.form = site.clone();
            self.sel_site = Some(i);
            self.password.clear();
            self.prefilled = false;
        }
    }

    /// Fills the password from the unlocked store (once per selected site).
    fn prefill(&mut self, shared: &Shared) {
        if self.prefilled || !self.form.save_password {
            return;
        }
        if let Some(store) = &shared.passwords {
            let key = store_key(&self.form.user, &self.form.host, self.form.port);
            if let Some(pw) = store.get(&key) {
                self.password = pw.to_string();
            }
            self.prefilled = true;
        }
    }

    fn save_site(&mut self, shared: &mut Shared) {
        let mut site = self.form.clone();
        if site.name.trim().is_empty() {
            site.name = site.host.clone();
        }
        self.form.name = site.name.clone();
        match self.sel_site {
            Some(i) if i < shared.settings.ftp_sites.len() => shared.settings.ftp_sites[i] = site,
            _ => {
                shared.settings.ftp_sites.push(site);
                self.sel_site = Some(shared.settings.ftp_sites.len() - 1);
            }
        }
        shared.send(AppEvent::SettingsChanged);
    }

    fn delete_site(&mut self, shared: &mut Shared) {
        if let Some(i) = self.sel_site.take()
            && i < shared.settings.ftp_sites.len()
        {
            shared.settings.ftp_sites.remove(i);
            shared.send(AppEvent::SettingsChanged);
        }
    }

    fn sites_ui(&mut self, ui: &mut egui::Ui, shared: &mut Shared) {
        ui.strong(t("Sites", "Sites"));
        let mut picked = None;
        egui::ScrollArea::vertical()
            .id_salt("ftp-sites")
            .max_height(130.0)
            .auto_shrink([false, true])
            .show(ui, |ui| {
                for (i, s) in shared.settings.ftp_sites.iter().enumerate() {
                    let label = if s.name.is_empty() { &s.host } else { &s.name };
                    if ui
                        .selectable_label(self.sel_site == Some(i), label)
                        .clicked()
                    {
                        picked = Some(i);
                    }
                }
            });
        if let Some(i) = picked {
            self.select_site(i, shared);
        }
        ui.horizontal(|ui| {
            if ui.button(t("New", "Nouveau")).clicked() {
                self.sel_site = None;
                self.form = blank_site();
                self.password.clear();
                self.prefilled = true;
            }
            if ui.button(tr("save")).clicked() && !self.form.host.trim().is_empty() {
                self.save_site(shared);
            }
            if ui
                .add_enabled(self.sel_site.is_some(), egui::Button::new(tr("cmd.delete")))
                .clicked()
            {
                self.delete_site(shared);
            }
        });
        ui.separator();
        self.form_ui(ui, shared);
        ui.separator();
        self.connect_buttons(ui, shared);
    }

    fn form_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        egui::Grid::new("ftp-form").num_columns(2).show(ui, |ui| {
            let f = &mut self.form;
            ui.label(tr("name"));
            ui.add(egui::TextEdit::singleline(&mut f.name).desired_width(150.0));
            ui.end_row();
            ui.label(t("Host", "Hôte"));
            ui.add(egui::TextEdit::singleline(&mut f.host).desired_width(150.0));
            ui.end_row();
            ui.label(t("Port (0 = 21)", "Port (0 = 21)"));
            ui.add(egui::DragValue::new(&mut f.port).range(0..=65535));
            ui.end_row();
            ui.label(t(
                "User (empty = anonymous)",
                "Utilisateur (vide = anonyme)",
            ));
            ui.add(egui::TextEdit::singleline(&mut f.user).desired_width(150.0));
            ui.end_row();
            ui.label(tr("password"));
            ui.add(
                egui::TextEdit::singleline(&mut self.password)
                    .password(true)
                    .desired_width(150.0),
            );
            ui.end_row();
            ui.label(t("Start folder", "Dossier de départ"));
            ui.add(egui::TextEdit::singleline(&mut f.remote_dir).desired_width(150.0));
            ui.end_row();
        });
        ui.checkbox(
            &mut self.form.secure,
            t("Explicit FTPS (TLS)", "FTPS explicite (TLS)"),
        );
        ui.checkbox(&mut self.form.passive, t("Passive mode", "Mode passif"));
        let unlocked = shared.passwords.is_some();
        ui.add_enabled(
            unlocked,
            egui::Checkbox::new(
                &mut self.form.save_password,
                t(
                    "Save password in the password manager",
                    "Enregistrer le mot de passe dans le gestionnaire",
                ),
            ),
        );
        if !unlocked {
            ui.weak(t(
                "Unlock the password manager first (Configuration → Passwords)",
                "Déverrouillez d'abord le gestionnaire de mots de passe (Configuration → Mots de passe)",
            ));
        }
        if !self.form.secure && !self.form.user.is_empty() {
            ui.weak(t(
                "Without TLS the password travels unencrypted.",
                "Sans TLS, le mot de passe circule en clair.",
            ));
        }
    }

    fn connect_buttons(&mut self, ui: &mut egui::Ui, shared: &mut Shared) {
        ui.horizontal(|ui| {
            let idle = self.state == State::Idle;
            if ui
                .add_enabled(idle, egui::Button::new(t("Connect", "Connexion")))
                .clicked()
            {
                self.connect(ui.ctx(), shared);
            }
            if ui
                .add_enabled(!idle, egui::Button::new(t("Disconnect", "Déconnexion")))
                .clicked()
            {
                self.disconnect();
            }
            if self.state == State::Connecting {
                ui.spinner();
            }
        });
    }

    // ---- connection -----------------------------------------------------------------

    fn connect(&mut self, ctx: &egui::Context, shared: &mut Shared) {
        if self.form.host.trim().is_empty() {
            self.log(t("Enter a host first.", "Indiquez d'abord un hôte."));
            return;
        }
        self.remember_password(shared);
        let handle = worker::spawn(self.form.clone(), self.password.clone(), ctx.clone());
        self.link = Some(handle);
        self.state = State::Connecting;
        self.pending = 1;
        self.entries.clear();
        self.selection.clear();
        self.input = None;
        self.log(format!(
            "{} {}…",
            t("Connecting to", "Connexion à"),
            self.form.host.trim()
        ));
    }

    /// Stores the password when asked to and the store is unlocked. Saving re-encrypts the
    /// store (about a second), so it only happens when the value changed.
    fn remember_password(&mut self, shared: &mut Shared) {
        if !self.form.save_password || self.password.is_empty() {
            return;
        }
        let key = store_key(&self.form.user, &self.form.host, self.form.port);
        let Some(store) = shared.passwords.as_mut() else {
            return;
        };
        if store.get(&key) == Some(self.password.as_str()) {
            return;
        }
        store.set(&key, &self.password);
        if let Err(e) = store.save() {
            let line = format!(
                "{}: {e}",
                t("Cannot save the password", "Mot de passe non enregistré")
            );
            self.log(line);
        }
    }

    fn disconnect(&mut self) {
        if let Some(link) = self.link.take() {
            let _ = link.cmd.send(Cmd::Quit);
        }
        self.reset_remote();
    }

    fn reset_remote(&mut self) {
        self.state = State::Idle;
        self.pending = 0;
        self.entries.clear();
        self.selection.clear();
        self.progress = None;
        self.input = None;
    }

    fn drain_events(&mut self, shared: &Shared) {
        loop {
            let ev = match self.link.as_ref().map(|l| l.ev.try_recv()) {
                Some(Ok(ev)) => ev,
                Some(Err(TryRecvError::Disconnected)) => {
                    self.link = None;
                    self.reset_remote();
                    return;
                }
                _ => return,
            };
            self.on_event(ev, shared);
        }
    }

    fn on_event(&mut self, ev: Ev, shared: &Shared) {
        match ev {
            Ev::Connected => {
                self.state = State::Connected;
                self.log(t("Connected.", "Connecté."));
            }
            Ev::Listing { pwd, mut entries } => {
                worker::sort_entries(&mut entries);
                if pwd != self.pwd {
                    self.selection.clear();
                } else {
                    self.selection
                        .retain(|n| entries.iter().any(|e| &e.name == n));
                }
                self.pwd = pwd;
                self.entries = entries;
            }
            Ev::Progress { name, bytes } => self.progress = Some((name, bytes)),
            Ev::Message(m) => self.log(m),
            Ev::Error(e) => self.log(format!("{}: {e}", tr("error"))),
            Ev::Done { reload_local } => {
                self.pending = self.pending.saturating_sub(1);
                if self.pending == 0 {
                    self.progress = None;
                }
                if reload_local {
                    shared.send(AppEvent::Reload);
                }
            }
            Ev::Disconnected => {
                self.link = None;
                self.reset_remote();
                self.log(t("Disconnected.", "Déconnecté."));
            }
        }
    }

    // ---- remote side ----------------------------------------------------------------

    fn selected_items(&self) -> Vec<Item> {
        self.entries
            .iter()
            .filter(|e| self.selection.contains(&e.name))
            .map(|e| Item {
                name: e.name.clone(),
                is_dir: e.is_dir,
            })
            .collect()
    }

    fn remote_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        if let Some(action) = self.toolbar(ui, shared) {
            self.run_action(action, shared);
        }
        self.input_ui(ui);
        ui.label(if self.connected() {
            self.pwd.as_str()
        } else {
            "—"
        });
        let out = self.table_ui(ui);
        self.apply_table(out);
    }

    fn toolbar(&self, ui: &mut egui::Ui, shared: &Shared) -> Option<Action> {
        let on = self.connected();
        let mut action = None;
        let mut btn = |ui: &mut egui::Ui, enabled: bool, text: &str, a: Action| {
            if ui.add_enabled(enabled, egui::Button::new(text)).clicked() {
                action = Some(a);
            }
        };
        ui.horizontal_wrapped(|ui| {
            btn(ui, on, t("Up", "Haut"), Action::Up);
            btn(ui, on, tr("cmd.refresh"), Action::Refresh);
            ui.separator();
            let can_down = on && shared.active_dir.is_some();
            btn(
                ui,
                can_down,
                t("Download →", "Télécharger →"),
                Action::Download,
            );
            let can_up = on && !shared.active_selection.is_empty();
            btn(ui, can_up, t("← Upload", "← Envoyer"), Action::Upload);
            ui.separator();
            btn(ui, on, tr("cmd.mkdir"), Action::NewFolder);
            btn(
                ui,
                on && self.selection.len() == 1,
                tr("cmd.rename"),
                Action::Rename,
            );
            btn(
                ui,
                on && !self.selection.is_empty(),
                tr("cmd.delete"),
                Action::Delete,
            );
            ui.separator();
            btn(ui, self.pending > 0 && on, tr("stop"), Action::Cancel);
            btn(
                ui,
                self.state != State::Idle,
                t("Disconnect", "Déconnexion"),
                Action::Disconnect,
            );
        });
        action
    }

    fn run_action(&mut self, action: Action, shared: &Shared) {
        match action {
            Action::Up => self.send(Cmd::Up),
            Action::Refresh => self.send(Cmd::List),
            Action::Download => {
                let items = self.selected_items();
                match (&shared.active_dir, items.is_empty()) {
                    (Some(dir), false) => self.send(Cmd::Download {
                        items,
                        local_dir: dir.clone(),
                    }),
                    _ => self.log(t(
                        "Select remote files first.",
                        "Sélectionnez d'abord des fichiers distants.",
                    )),
                }
            }
            Action::Upload => self.send(Cmd::Upload {
                paths: shared.active_selection.clone(),
            }),
            Action::NewFolder => self.open_input(InputKind::NewFolder, String::new()),
            Action::Rename => {
                if let Some(name) = self.selection.iter().next().cloned() {
                    self.open_input(InputKind::Rename(name.clone()), name);
                }
            }
            Action::Delete => {
                let items = self.selected_items();
                if !items.is_empty() {
                    self.open_input(InputKind::ConfirmDelete(items), String::new());
                }
            }
            Action::Disconnect => self.disconnect(),
            Action::Cancel => {
                if let Some(link) = &self.link {
                    link.cancel.store(true, Ordering::Relaxed);
                }
            }
        }
    }

    fn open_input(&mut self, kind: InputKind, text: String) {
        self.input = Some(Input {
            kind,
            text,
            fresh: true,
        });
    }

    fn input_ui(&mut self, ui: &mut egui::Ui) {
        let Some(mut input) = self.input.take() else {
            return;
        };
        let mut confirmed = false;
        let mut cancelled = false;
        ui.horizontal(|ui| {
            match &input.kind {
                InputKind::ConfirmDelete(items) => {
                    ui.label(format!(
                        "{} ({})?",
                        t("Delete the selection", "Supprimer la sélection"),
                        items.len()
                    ));
                }
                kind => {
                    ui.label(if matches!(kind, InputKind::NewFolder) {
                        t("New folder:", "Nouveau dossier :")
                    } else {
                        t("Rename to:", "Renommer en :")
                    });
                    let resp = ui.text_edit_singleline(&mut input.text);
                    if input.fresh {
                        resp.request_focus();
                        input.fresh = false;
                    }
                    if resp.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) {
                        confirmed = true;
                    }
                }
            }
            confirmed |= ui.button(tr("ok")).clicked();
            cancelled |= ui.button(tr("cancel")).clicked();
        });
        if confirmed {
            self.run_input(input);
        } else if !cancelled {
            self.input = Some(input);
        }
    }

    fn run_input(&mut self, input: Input) {
        let name = input.text.trim().to_string();
        match input.kind {
            InputKind::NewFolder if !name.is_empty() => self.send(Cmd::Mkdir(name)),
            InputKind::Rename(from) if !name.is_empty() && name != from => {
                self.send(Cmd::Rename { from, to: name });
            }
            InputKind::ConfirmDelete(items) => {
                for it in items {
                    self.send(Cmd::Delete {
                        name: it.name,
                        is_dir: it.is_dir,
                    });
                }
            }
            _ => {}
        }
    }

    fn apply_table(&mut self, out: table::Out) {
        if let Some(idx) = out.double {
            if idx == 0 {
                self.send(Cmd::Up);
            } else if let Some(e) = self.entries.get(idx - 1)
                && e.is_dir
            {
                let path = join_remote(&self.pwd, &e.name);
                self.send(Cmd::Cd(path));
            }
        } else if let Some((idx, add)) = out.click
            && idx > 0
            && let Some(e) = self.entries.get(idx - 1)
        {
            let name = e.name.clone();
            if !add {
                self.selection.clear();
            }
            if !(add && self.selection.remove(&name)) {
                self.selection.insert(name);
            }
        }
    }

    fn log_ui(&self, ui: &mut egui::Ui) {
        egui::ScrollArea::vertical()
            .id_salt("ftp-log")
            .stick_to_bottom(true)
            .auto_shrink([false, false])
            .show(ui, |ui| {
                for line in &self.log {
                    ui.label(egui::RichText::new(line).monospace().small());
                }
            });
    }

    fn status_ui(&self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            if self.pending > 0 {
                ui.spinner();
            }
            match (&self.progress, self.state) {
                (Some((name, bytes)), _) => ui.label(format!("{name}: {}", human_size(*bytes))),
                (None, State::Connected) => ui.label(format!(
                    "{} {}",
                    self.entries.len(),
                    t("entries", "éléments")
                )),
                (None, State::Connecting) => ui.label(t("Connecting…", "Connexion…")),
                (None, State::Idle) => ui.label(t("Not connected", "Non connecté")),
            };
        });
    }

    fn ui(&mut self, ui: &mut egui::Ui, shared: &mut Shared) {
        StripBuilder::new(ui)
            .size(Size::remainder())
            .size(Size::exact(100.0))
            .size(Size::exact(22.0))
            .vertical(|mut strip| {
                strip.strip(|b| {
                    b.size(Size::exact(270.0))
                        .size(Size::remainder())
                        .horizontal(|mut s| {
                            // The site form is taller than a small window: let it scroll.
                            s.cell(|ui| {
                                egui::ScrollArea::vertical()
                                    .id_salt("ftp-left")
                                    .show(ui, |ui| self.sites_ui(ui, shared));
                            });
                            s.cell(|ui| self.remote_ui(ui, shared));
                        });
                });
                strip.cell(|ui| {
                    ui.separator();
                    self.log_ui(ui);
                });
                strip.cell(|ui| self.status_ui(ui));
            });
    }
}

impl Default for FtpWindow {
    fn default() -> Self {
        Self::new()
    }
}

impl Drop for FtpWindow {
    fn drop(&mut self) {
        self.disconnect();
    }
}

impl ToolWindow for FtpWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        self.drain_events(shared);
        self.prefill(shared);
        let mut open = true;
        super::tool_window(tr("cmd.ftp"), ctx, [940.0, 580.0])
            .id(self.id)
            .open(&mut open)
            .show(ctx, |ui| self.ui(ui, shared));
        if !open {
            self.disconnect();
        }
        open
    }
}

fn blank_site() -> FtpSite {
    FtpSite {
        port: 21,
        passive: true,
        ..FtpSite::default()
    }
}
