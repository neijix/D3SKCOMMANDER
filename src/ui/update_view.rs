//! "Check for updates": the latest release, its notes and an Update button that
//! downloads the file for this install (package, AppImage, app bundle, setup or plain
//! program), installs it and restarts the app. Also the quiet check at start-up that
//! lights the menu-bar button.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, channel};
use std::thread;

use eframe::egui::{self, RichText};

use super::icons;
use super::{AppEvent, Shared, ToolWindow, window_id};
use crate::core::fs::human_size;
use crate::core::update::apply::{self, Relaunch};
use crate::core::update::{self, CURRENT_VERSION, Facts, InstallKind, Release};
use crate::i18n::t;

/// Asks GitHub for the latest release on a background thread.
fn fetch(ctx: &egui::Context) -> Receiver<Result<Release, String>> {
    let (tx, rx) = channel();
    let ctx = ctx.clone();
    thread::spawn(move || {
        let result = update::latest_release_url(update::REPOSITORY)
            .ok_or_else(|| format!("not a GitHub repository: {}", update::REPOSITORY))
            .and_then(|url| apply::fetch_latest(&url));
        let _ = tx.send(result);
        ctx.request_repaint();
    });
    rx
}

/// The start-up check: a newer release, once found, stays in `found`.
#[derive(Default)]
pub struct StartupCheck {
    rx: Option<Receiver<Result<Release, String>>>,
    pub found: Option<Release>,
}

impl StartupCheck {
    /// Starts the check, except in a source build (its owner updates with git).
    pub fn start(ctx: &egui::Context) -> StartupCheck {
        if update::detect(&Facts::current()) == InstallKind::Source {
            return StartupCheck::default();
        }
        StartupCheck {
            rx: Some(fetch(ctx)),
            found: None,
        }
    }

    pub fn poll(&mut self) {
        let Some(rx) = &self.rx else { return };
        if let Ok(result) = rx.try_recv() {
            self.rx = None;
            self.found = result
                .ok()
                .filter(|r| update::is_newer(&r.version, CURRENT_VERSION));
        }
    }
}

enum Msg {
    Checked(Result<Release, String>),
    Progress(u64, u64),
    Installing,
    Done(Result<Relaunch, String>),
}

enum State {
    Checking,
    Failed(String),
    Ready(Release),
    Downloading(u64, u64),
    Installing,
    Restarting,
}

pub struct UpdateWindow {
    id: egui::Id,
    kind: InstallKind,
    state: State,
    rx: Option<Receiver<Msg>>,
    cancel: Arc<AtomicBool>,
}

impl UpdateWindow {
    /// Opens the window and checks (again) for the latest release.
    pub fn new(ctx: &egui::Context) -> UpdateWindow {
        let (tx, rx) = channel();
        let inner = fetch(ctx);
        thread::spawn(move || {
            if let Ok(r) = inner.recv() {
                let _ = tx.send(Msg::Checked(r));
            }
        });
        UpdateWindow {
            id: window_id("update"),
            kind: update::detect(&Facts::current()),
            state: State::Checking,
            rx: Some(rx),
            cancel: Arc::new(AtomicBool::new(false)),
        }
    }

    fn start_install(&mut self, release: &Release, ctx: &egui::Context) {
        let os = std::env::consts::OS;
        let arch = std::env::consts::ARCH;
        let Some(asset) = update::pick_asset(release, &self.kind, os, arch).cloned() else {
            self.state = State::Failed(format!(
                "{} ({os}/{arch})",
                t(
                    "This release has no file for this installation",
                    "Cette version n'a pas de fichier pour cette installation"
                )
            ));
            return;
        };
        let size = asset.size;
        let (tx, rx) = channel();
        let kind = self.kind.clone();
        let cancel = Arc::clone(&self.cancel);
        let ctx = ctx.clone();
        thread::spawn(move || {
            let dir = std::env::temp_dir().join("d3skcommander-update");
            let mut progress = |done, total| {
                let _ = tx.send(Msg::Progress(done, total));
                ctx.request_repaint();
                !cancel.load(Ordering::Relaxed)
            };
            let result = apply::download(&asset, &dir, &mut progress).and_then(|file| {
                let _ = tx.send(Msg::Installing);
                ctx.request_repaint();
                apply::install(&kind, &file)
            });
            let _ = tx.send(Msg::Done(result));
            ctx.request_repaint();
        });
        self.rx = Some(rx);
        self.state = State::Downloading(0, size);
    }

    fn poll(&mut self, shared: &Shared) {
        let Some(rx) = &self.rx else { return };
        for msg in rx.try_iter().collect::<Vec<_>>() {
            self.state = match msg {
                Msg::Checked(Ok(r)) => State::Ready(r),
                Msg::Checked(Err(e)) | Msg::Done(Err(e)) => State::Failed(e),
                Msg::Progress(done, total) => State::Downloading(done, total),
                Msg::Installing => State::Installing,
                Msg::Done(Ok(relaunch)) => {
                    shared.send(AppEvent::Restart(relaunch));
                    State::Restarting
                }
            };
        }
    }

    fn body(&mut self, ui: &mut egui::Ui) {
        ui.label(format!(
            "{}: {CURRENT_VERSION}  ·  {}",
            t("Installed version", "Version installée"),
            kind_label(&self.kind)
        ));
        ui.separator();
        let mut install = None;
        match &self.state {
            State::Checking => busy(
                ui,
                t(
                    "Looking for a new version…",
                    "Recherche d'une nouvelle version…",
                ),
            ),
            State::Failed(e) => {
                ui.colored_label(
                    ui.visuals().warn_fg_color,
                    format!("{}  {e}", icons::WARNING),
                );
            }
            State::Ready(r) if !update::is_newer(&r.version, CURRENT_VERSION) => {
                let text = t(
                    "You have the latest version.",
                    "Vous avez la dernière version.",
                );
                ui.label(format!("{}  {text}", icons::OK));
            }
            State::Ready(r) => {
                if release_ui(ui, r, self.kind != InstallKind::Source) {
                    install = Some(r.clone());
                }
            }
            State::Downloading(done, total) => {
                if progress_ui(ui, *done, *total) {
                    self.cancel.store(true, Ordering::Relaxed);
                }
            }
            State::Installing => busy(ui, t("Installing…", "Installation…")),
            State::Restarting => {
                ui.label(format!(
                    "{}  {}",
                    icons::RESTART,
                    t("Restarting…", "Redémarrage…")
                ));
            }
        }
        if let Some(r) = install {
            self.start_install(&r, ui.ctx());
        }
    }
}

fn busy(ui: &mut egui::Ui, text: &str) {
    ui.horizontal(|ui| {
        ui.spinner();
        ui.label(text);
    });
}

/// The new release, its notes and the buttons; true when "Update now" was clicked.
fn release_ui(ui: &mut egui::Ui, r: &Release, can_install: bool) -> bool {
    let title = format!(
        "{}  {} {}",
        icons::NEWER,
        t("New version", "Nouvelle version"),
        r.version
    );
    ui.label(RichText::new(title).strong().size(16.0));
    egui::ScrollArea::vertical()
        .max_height(220.0)
        .show(ui, |ui| {
            ui.label(r.notes.trim());
        });
    let mut clicked = false;
    ui.horizontal(|ui| {
        let text = format!("{}  {}", icons::DOWNLOAD, t("Update now", "Mettre à jour"));
        clicked = ui
            .add_enabled(can_install, egui::Button::new(RichText::new(text).strong()))
            .clicked();
        let page = format!(
            "{}  {}",
            icons::OPEN_PAGE,
            t("Release page", "Page de la version")
        );
        if ui.button(page).clicked() {
            let _ = open::that(&r.page);
        }
    });
    clicked
}

/// The download bar; true when "Cancel" was clicked.
fn progress_ui(ui: &mut egui::Ui, done: u64, total: u64) -> bool {
    let f = if total > 0 {
        done as f32 / total as f32
    } else {
        0.0
    };
    ui.label(t("Downloading…", "Téléchargement…"));
    let text = format!("{} / {}", human_size(done), human_size(total));
    ui.add(egui::ProgressBar::new(f).text(text));
    ui.button(t("Cancel", "Annuler")).clicked()
}

fn kind_label(kind: &InstallKind) -> &'static str {
    match kind {
        InstallKind::AppImage(_) => "AppImage",
        InstallKind::Deb(_) => t("Debian package", "paquet Debian"),
        InstallKind::MacApp(_) => t("macOS app", "app macOS"),
        InstallKind::WindowsSetup(_) => t("Windows installer", "installeur Windows"),
        InstallKind::Portable(_) => t("portable program", "programme portable"),
        InstallKind::Source => t(
            "built from source (update with git pull)",
            "compilé depuis les sources (mettre à jour avec git pull)",
        ),
    }
}

impl ToolWindow for UpdateWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        self.poll(shared);
        let mut open = true;
        super::tool_window(t("Check for updates", "Mises à jour"), ctx, [460.0, 320.0])
            .id(self.id)
            .open(&mut open)
            .collapsible(false)
            .show(ctx, |ui| self.body(ui));
        if !open {
            self.cancel.store(true, Ordering::Relaxed);
        }
        open
    }
}
