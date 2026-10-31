//! The egui front end: the main window (two panels, menus, command line, function-key
//! bar), modal dialogs, background jobs and the tool windows.
//!
//! Tool windows (lister, editor, compare, sync, search, multi-rename, FTP, settings)
//! implement [`ToolWindow`]; they read and change the app through [`Shared`] and send
//! [`AppEvent`]s for anything that touches the panels.

mod actions;
pub mod compare_view;
mod dialogs;
pub mod editor;
pub mod ftp_view;
pub mod jobs;
pub mod keys;
pub mod lister;
mod menu;
pub mod panel;
mod panel_view;
pub mod rename_view;
pub mod search_view;
pub mod settings_view;
mod smoke;
pub mod sync_view;
mod tools;

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::{Receiver, Sender, channel};
use std::time::{Duration, Instant, SystemTime};

use eframe::egui;

use crate::config::{self, PanelConfig, Settings};
use crate::core::crypt::PasswordStore;
use crate::core::fileops;
use crate::core::fs::{self as cfs, Drive};
use crate::i18n::{self, tr};
use dialogs::Dialog;
use jobs::Jobs;
use panel::{Location, PanelState};

/// Something a tool window or a background thread asks the main window to do.
#[derive(Debug)]
pub enum AppEvent {
    /// Re-read both panels.
    Reload,
    Status(String),
    /// Show these paths in the active panel (search results, sync differences).
    FeedToPanel {
        title: String,
        paths: Vec<PathBuf>,
    },
    /// Open the file's folder in the active panel and put the cursor on it.
    GoToFile(PathBuf),
    /// Open a folder in the active panel.
    OpenDir(PathBuf),
    CompareFiles(PathBuf, PathBuf),
    View(PathBuf),
    Edit(PathBuf),
    /// A computed folder size (Space / Ctrl+L).
    DirSize(PathBuf, u64),
    Error(String),
    /// `Shared::settings` changed: re-apply language, look and panels.
    SettingsChanged,
}

/// What tool windows may read or change.
pub struct Shared {
    pub settings: Settings,
    /// Unlocked password store, `None` until the master password was entered.
    pub passwords: Option<PasswordStore>,
    pub tx: Sender<AppEvent>,
    /// The active panel's folder (`None` inside an archive or a result list).
    pub active_dir: Option<PathBuf>,
    /// The other panel's folder.
    pub other_dir: Option<PathBuf>,
    /// The active panel's targets (selection, else the cursor entry), as real paths.
    pub active_selection: Vec<PathBuf>,
}

impl Shared {
    pub fn send(&self, ev: AppEvent) {
        let _ = self.tx.send(ev);
    }
}

/// A non-modal window living beside the panels.
pub trait ToolWindow {
    /// Draws the window; returns false once the user closed it.
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool;
}

/// A fresh id for a tool window, so two windows of the same kind never clash.
pub fn window_id(kind: &str) -> egui::Id {
    static NEXT: AtomicU64 = AtomicU64::new(0);
    egui::Id::unique((kind.to_string(), NEXT.fetch_add(1, Ordering::Relaxed)))
}

/// The quick view panel (Ctrl+Q): the other panel previews the active panel's file.
pub(crate) struct QuickView {
    pub(crate) path: Option<PathBuf>,
    pub(crate) lister: Option<lister::Lister>,
}

pub struct App {
    pub(crate) shared: Shared,
    rx: Receiver<AppEvent>,
    settings_path: PathBuf,
    pub(crate) panels: [PanelState; 2],
    /// 0 = left, 1 = right.
    pub(crate) active: usize,
    pub(crate) jobs: Jobs,
    pub(crate) dialog: Option<Dialog>,
    pub(crate) windows: Vec<Box<dyn ToolWindow>>,
    pub(crate) status: String,
    pub(crate) quick_view: Option<QuickView>,
    pub(crate) command_line: String,
    pub(crate) drives: Vec<Drive>,
    drives_at: Option<Instant>,
    dir_stamps: HashMap<PathBuf, Option<SystemTime>>,
    stamps_at: Instant,
    last_dir: Option<PathBuf>,
    applied_look: Option<(bool, u32)>,
    /// The egui context, for background threads that must wake the UI.
    pub(crate) ctx: Option<egui::Context>,
    /// Focus the quick-filter field / the command line on the next frame.
    pub(crate) focus_filter: bool,
    pub(crate) focus_cmdline: bool,
    smoke: Option<smoke::Smoke>,
}

impl App {
    pub fn new(cc: &eframe::CreationContext<'_>) -> App {
        egui_extras::install_image_loaders(&cc.egui_ctx);
        let settings_path = config::settings_path();
        let settings = Settings::load(&settings_path);
        Self::with_settings(settings, settings_path)
    }

    /// Builds the app from explicit settings (also used by tests).
    pub fn with_settings(settings: Settings, settings_path: PathBuf) -> App {
        i18n::set_lang(settings.lang);
        apply_log_setting(&settings);
        let home = cfs::home_dir();
        let make = |pc: &PanelConfig| {
            let tabs: Vec<PathBuf> = if pc.tabs.is_empty() {
                vec![home.clone()]
            } else {
                pc.tabs.clone()
            };
            PanelState::new(&tabs, pc.active_tab, pc.sort, settings.show_hidden)
        };
        let panels = [make(&settings.left), make(&settings.right)];
        let (tx, rx) = channel();
        App {
            shared: Shared {
                settings,
                passwords: None,
                tx,
                active_dir: None,
                other_dir: None,
                active_selection: Vec::new(),
            },
            rx,
            settings_path,
            panels,
            active: 0,
            jobs: Jobs::default(),
            dialog: None,
            windows: Vec::new(),
            status: tr("st.ready").into(),
            quick_view: None,
            command_line: String::new(),
            drives: Vec::new(),
            drives_at: None,
            dir_stamps: HashMap::new(),
            stamps_at: Instant::now(),
            last_dir: None,
            applied_look: None,
            ctx: None,
            focus_filter: false,
            focus_cmdline: false,
            smoke: smoke::Smoke::from_env(),
        }
    }

    pub(crate) fn show_hidden(&self) -> bool {
        self.shared.settings.show_hidden
    }

    pub(crate) fn active_panel(&mut self) -> &mut PanelState {
        &mut self.panels[self.active]
    }

    pub(crate) fn other_panel(&mut self) -> &mut PanelState {
        &mut self.panels[1 - self.active]
    }

    /// The active panel's folder when it shows a real folder.
    pub(crate) fn active_dir(&self) -> Option<PathBuf> {
        match &self.panels[self.active].tab().location {
            Location::Local(p) => Some(p.clone()),
            _ => None,
        }
    }

    pub(crate) fn other_dir(&self) -> Option<PathBuf> {
        match &self.panels[1 - self.active].tab().location {
            Location::Local(p) => Some(p.clone()),
            _ => None,
        }
    }

    pub(crate) fn reload_all(&mut self) {
        let hidden = self.show_hidden();
        for p in &mut self.panels {
            for tab in &mut p.tabs {
                tab.reload(hidden);
            }
        }
    }

    pub(crate) fn save_settings(&mut self) {
        let store = |p: &PanelState, pc: &mut PanelConfig| {
            pc.tabs = p.tab_dirs();
            pc.active_tab = p.active_tab.min(pc.tabs.len().saturating_sub(1));
            pc.sort = p.tab().sort;
        };
        store(&self.panels[0], &mut self.shared.settings.left);
        store(&self.panels[1], &mut self.shared.settings.right);
        if let Err(e) = self.shared.settings.save(&self.settings_path) {
            self.status = format!("{}: {e}", tr("error"));
        }
    }

    pub(crate) fn drives(&mut self) -> &[Drive] {
        let stale = self
            .drives_at
            .is_none_or(|t| t.elapsed() > Duration::from_secs(5));
        if stale {
            self.drives = cfs::drives();
            self.drives_at = Some(Instant::now());
        }
        &self.drives
    }

    fn apply_look(&mut self, ctx: &egui::Context) {
        let s = &self.shared.settings;
        let look = (s.dark_mode, s.font_size.to_bits());
        if self.applied_look == Some(look) {
            return;
        }
        self.applied_look = Some(look);
        ctx.set_visuals(if s.dark_mode {
            egui::Visuals::dark()
        } else {
            egui::Visuals::light()
        });
        ctx.set_zoom_factor((s.font_size / 14.0).clamp(0.6, 2.5));
    }

    fn drain_events(&mut self) {
        let events: Vec<AppEvent> = self.rx.try_iter().collect();
        for ev in events {
            self.on_event(ev);
        }
    }

    fn on_event(&mut self, ev: AppEvent) {
        let hidden = self.show_hidden();
        match ev {
            AppEvent::Reload => self.reload_all(),
            AppEvent::Status(s) => self.status = s,
            AppEvent::FeedToPanel { title, paths } => {
                let loc = Location::List {
                    title,
                    paths: std::sync::Arc::new(paths),
                };
                self.active_panel().tab_mut().go(loc, hidden);
            }
            AppEvent::GoToFile(path) => self.go_to_file(&path),
            AppEvent::OpenDir(dir) => {
                self.active_panel()
                    .tab_mut()
                    .go(Location::Local(dir), hidden);
            }
            AppEvent::CompareFiles(a, b) => {
                self.windows
                    .push(Box::new(compare_view::CompareWindow::new(a, b)));
            }
            AppEvent::View(p) => self.windows.push(Box::new(lister::ListerWindow::new(p))),
            AppEvent::Edit(p) => self.edit_file(p),
            AppEvent::DirSize(dir, size) => {
                for p in &mut self.panels {
                    for tab in &mut p.tabs {
                        if tab.entries.iter().any(|e| e.path == dir) {
                            tab.dir_sizes.insert(dir.clone(), size);
                        }
                    }
                }
            }
            AppEvent::Error(e) => self.dialog = Some(Dialog::message(tr("error"), e)),
            AppEvent::SettingsChanged => {
                i18n::set_lang(self.shared.settings.lang);
                apply_log_setting(&self.shared.settings);
                self.applied_look = None;
                self.reload_all();
                self.save_settings();
            }
        }
    }

    /// Opens the file's folder in the active panel and puts the cursor on it.
    pub(crate) fn go_to_file(&mut self, path: &Path) {
        let hidden = self.show_hidden();
        let (dir, file) = if path.is_dir() {
            (path.to_path_buf(), None)
        } else {
            (
                path.parent().map(Path::to_path_buf).unwrap_or_default(),
                Some(path),
            )
        };
        let tab = self.active_panel().tab_mut();
        if tab.go(Location::Local(dir), hidden)
            && let Some(f) = file
        {
            tab.select_path(f);
        }
    }

    fn finish_jobs(&mut self, ctx: &egui::Context) {
        let outcomes = self.jobs.ui(ctx);
        if outcomes.is_empty() {
            return;
        }
        self.reload_all();
        for o in outcomes {
            self.status = format!("{}: {}", o.title, o.summary);
            if !o.errors.is_empty() {
                let mut text = o.errors.iter().take(40).cloned().collect::<Vec<_>>();
                if o.errors.len() > 40 {
                    text.push(format!("… (+{})", o.errors.len() - 40));
                }
                self.dialog = Some(Dialog::message(&o.title, text.join("\n")));
            }
        }
    }

    /// Re-reads a panel whose folder changed on disk (checked every 1.5 s).
    fn auto_refresh(&mut self) {
        if self.stamps_at.elapsed() < Duration::from_millis(1500) || !self.jobs.is_empty() {
            return;
        }
        self.stamps_at = Instant::now();
        let hidden = self.show_hidden();
        for p in &mut self.panels {
            let tab = p.tab_mut();
            let Location::Local(dir) = &tab.location else {
                continue;
            };
            if tab.branch {
                continue;
            }
            let stamp = std::fs::metadata(dir).and_then(|m| m.modified()).ok();
            let dir = dir.clone();
            if let Some(old) = self.dir_stamps.insert(dir, stamp)
                && old != stamp
            {
                tab.reload(hidden);
            }
        }
    }

    /// Files dropped from the OS are copied into the active panel's folder.
    fn handle_dropped_files(&mut self, ctx: &egui::Context) {
        let dropped: Vec<PathBuf> = ctx.input(|i| {
            i.raw
                .dropped_files
                .iter()
                .map(|f| f.path().to_path_buf())
                .filter(|p| !p.as_os_str().is_empty())
                .collect()
        });
        if dropped.is_empty() {
            return;
        }
        if let Some(dest) = self.active_dir() {
            let req = fileops::OpRequest {
                kind: fileops::OpKind::Copy,
                sources: dropped,
                dest_dir: Some(dest),
                rename_to: None,
            };
            self.jobs.start_file_op(tr("copying").into(), req, ctx);
        }
    }

    /// Remembers every visited folder for Alt+Down.
    fn track_history(&mut self) {
        let dir = self.active_dir();
        if dir != self.last_dir {
            if let Some(d) = &dir {
                self.shared.settings.push_history(d);
            }
            self.last_dir = dir;
        }
    }

    fn update_shared(&mut self) {
        self.shared.active_dir = self.active_dir();
        self.shared.other_dir = self.other_dir();
        self.shared.active_selection = self.panels[self.active].tab().target_paths();
    }

    fn windows_ui(&mut self, ctx: &egui::Context) {
        let mut windows = std::mem::take(&mut self.windows);
        windows.retain_mut(|w| w.show(ctx, &mut self.shared));
        // Windows opened while drawing (none today) come after the existing ones.
        windows.append(&mut self.windows);
        self.windows = windows;
    }

    fn quick_view_ui(&mut self, ui: &mut egui::Ui) {
        let current = self.panels[self.active]
            .tab()
            .current()
            .filter(|e| !e.is_parent && !e.is_dir)
            .map(|e| e.path.clone())
            .filter(|_| {
                matches!(
                    self.panels[self.active].tab().location,
                    Location::Local(_) | Location::List { .. }
                )
            });
        let Some(qv) = &mut self.quick_view else {
            return;
        };
        if qv.path != current {
            qv.lister = current.as_deref().map(lister::Lister::open);
            qv.path = current;
        }
        ui.strong(format!(
            "{} — {}",
            tr("cmd.quick_view"),
            qv.path
                .as_deref()
                .and_then(Path::file_name)
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_default()
        ));
        ui.separator();
        match &mut qv.lister {
            Some(l) => l.ui(ui),
            None => {
                ui.weak("—");
            }
        }
    }
}

fn apply_log_setting(s: &Settings) {
    fileops::set_log_file(s.log_operations.then(config::log_path));
}

impl eframe::App for App {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        if self.ctx.is_none() {
            self.ctx = Some(ctx.clone());
        }
        self.apply_look(&ctx);
        self.drain_events();
        self.auto_refresh();
        self.handle_dropped_files(&ctx);
        self.update_shared();
        if let Some(cmd) = self.smoke.as_mut().and_then(smoke::Smoke::take_command)
            && let Some(c) = keys::Command::from_name(&cmd)
        {
            self.run(c, &ctx);
        }
        let modal_open = self.dialog.is_some();
        if !modal_open && !ctx.egui_wants_keyboard_input() {
            self.handle_keys(&ctx);
        }

        egui::Panel::top("menu").show(ui, |ui| self.menu_bar(ui));
        egui::Panel::bottom("fkeys").show(ui, |ui| self.fkey_bar(ui));
        egui::Panel::bottom("cmdline").show(ui, |ui| self.command_line_ui(ui));
        egui::CentralPanel::default().show(ui, |ui| self.panels_ui(ui));

        self.dialog_ui(&ctx);
        self.windows_ui(&ctx);
        self.finish_jobs(&ctx);
        self.track_history();
        if let Some(s) = &mut self.smoke {
            s.frame(&ctx);
        }
        if ctx.input(|i| i.viewport().close_requested()) {
            self.save_settings();
        }
    }

    fn on_exit(&mut self) {
        self.save_settings();
    }
}

/// Opens the main window.
pub fn run() -> eframe::Result {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title("d3skcommander")
            .with_inner_size([1280.0, 800.0])
            .with_min_inner_size([640.0, 400.0])
            .with_drag_and_drop(true),
        ..Default::default()
    };
    eframe::run_native(
        "d3skcommander",
        options,
        Box::new(|cc| Ok(Box::new(App::new(cc)))),
    )
}
