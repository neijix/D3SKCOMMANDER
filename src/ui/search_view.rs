//! Find files: a form that builds a `SearchQuery`, a live result table fed by the search
//! thread, and actions on the hits.

use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, TryRecvError};
use std::time::{Duration, SystemTime};

use eframe::egui;
use egui_extras::{Column, TableBuilder};

use super::picker::{FolderPick, folder_field};
use super::{AppEvent, Shared, ToolWindow};
use crate::core::fs::{format_time, grouped};
use crate::core::search::{SearchEvent, SearchHit, SearchQuery, spawn};
use crate::core::splitter::parse_size;
use crate::i18n::{t, tr};

/// Events handled per frame, so a flood of hits cannot freeze the UI.
const EVENTS_PER_FRAME: usize = 4000;
/// Errors kept for display.
const MAX_ERRORS: usize = 50;
const SECS_PER_DAY: u64 = 86_400;

/// Everything the user types or ticks; survives re-runs.
#[derive(Clone, Debug)]
struct Form {
    /// One or more roots separated by ';'.
    roots: String,
    name: String,
    name_regex: bool,
    text: String,
    text_regex: bool,
    case_sensitive: bool,
    whole_words: bool,
    not_containing: bool,
    depth_limited: bool,
    depth: usize,
    include_hidden: bool,
    files: bool,
    dirs: bool,
    min_size: String,
    max_size: String,
    newer_days: String,
    older_days: String,
}

impl Form {
    fn new(root: &Path) -> Form {
        Form {
            roots: root.display().to_string(),
            name: "*".into(),
            name_regex: false,
            text: String::new(),
            text_regex: false,
            case_sensitive: false,
            whole_words: false,
            not_containing: false,
            depth_limited: false,
            depth: 1,
            include_hidden: false,
            files: true,
            dirs: true,
            min_size: String::new(),
            max_size: String::new(),
            newer_days: String::new(),
            older_days: String::new(),
        }
    }
}

fn split_roots(s: &str) -> Vec<PathBuf> {
    s.split(';')
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .map(PathBuf::from)
        .collect()
}

/// An empty field means "no bound".
fn optional_size(s: &str) -> Result<Option<u64>, String> {
    if s.trim().is_empty() {
        return Ok(None);
    }
    parse_size(s).map(Some)
}

/// `now` minus N days; an empty field means "no bound".
fn days_ago(s: &str, now: SystemTime) -> Result<Option<SystemTime>, String> {
    let s = s.trim();
    if s.is_empty() {
        return Ok(None);
    }
    let days: u64 = s.parse().map_err(|_| {
        format!(
            "{}: {s:?}",
            t("invalid number of days", "nombre de jours invalide")
        )
    })?;
    let delta = Duration::from_secs(days.saturating_mul(SECS_PER_DAY));
    now.checked_sub(delta)
        .map(Some)
        .ok_or_else(|| format!("{}: {s:?}", t("too many days", "trop de jours")))
}

/// Turns the form into a query (`now` is a parameter so tests are deterministic).
fn build_query(f: &Form, now: SystemTime) -> Result<SearchQuery, String> {
    let roots = split_roots(&f.roots);
    if roots.is_empty() {
        return Err(t("No folder to search in", "Aucun dossier où chercher").into());
    }
    if !f.files && !f.dirs {
        return Err(t(
            "Tick files and/or folders",
            "Cochez fichiers et/ou dossiers",
        )
        .into());
    }
    let (min_size, max_size) = (optional_size(&f.min_size)?, optional_size(&f.max_size)?);
    if let (Some(lo), Some(hi)) = (min_size, max_size)
        && lo > hi
    {
        return Err(t(
            "Minimum size exceeds maximum",
            "Taille min supérieure à la max",
        )
        .into());
    }
    let name = f.name.trim();
    Ok(SearchQuery {
        roots,
        name: if name.is_empty() {
            "*".into()
        } else {
            name.into()
        },
        name_is_regex: f.name_regex,
        content: (!f.text.is_empty()).then(|| f.text.clone()),
        content_is_regex: f.text_regex,
        case_sensitive: f.case_sensitive,
        whole_words: f.whole_words,
        not_containing: f.not_containing,
        max_depth: f.depth_limited.then_some(f.depth.max(1)),
        include_hidden: f.include_hidden,
        min_size,
        max_size,
        newer_than: days_ago(&f.newer_days, now)?,
        older_than: days_ago(&f.older_days, now)?,
        files: f.files,
        dirs: f.dirs,
        ..SearchQuery::default()
    })
}

struct Running {
    rx: Receiver<SearchEvent>,
    cancel: Arc<AtomicBool>,
}

pub struct SearchWindow {
    id: egui::Id,
    form: Form,
    run: Option<Running>,
    hits: Vec<SearchHit>,
    selected: Option<usize>,
    scanned: u64,
    current: PathBuf,
    /// `(hits, cancelled)` of the last finished run.
    finished: Option<(u64, bool)>,
    errors: Vec<String>,
    close: bool,
    root_pick: FolderPick,
}

impl SearchWindow {
    pub fn new(root: PathBuf) -> Self {
        Self {
            id: super::window_id("SearchWindow"),
            form: Form::new(&root),
            run: None,
            hits: Vec::new(),
            selected: None,
            scanned: 0,
            current: PathBuf::new(),
            finished: None,
            errors: Vec::new(),
            close: false,
            root_pick: FolderPick::default(),
        }
    }

    fn stop(&mut self) {
        if let Some(r) = &self.run {
            r.cancel.store(true, Ordering::Relaxed);
        }
    }

    fn start(&mut self) {
        self.stop();
        self.run = None;
        self.hits.clear();
        self.selected = None;
        self.scanned = 0;
        self.current = PathBuf::new();
        self.finished = None;
        self.errors.clear();
        match build_query(&self.form, SystemTime::now()) {
            Ok(q) => {
                let cancel = Arc::new(AtomicBool::new(false));
                let rx = spawn(q, Arc::clone(&cancel));
                self.run = Some(Running { rx, cancel });
            }
            Err(e) => self.errors.push(e),
        }
    }

    fn poll(&mut self, ctx: &egui::Context) {
        let Some(run) = &self.run else {
            return;
        };
        let mut ended = false;
        for _ in 0..EVENTS_PER_FRAME {
            match run.rx.try_recv() {
                Ok(SearchEvent::Hit(h)) => self.hits.push(h),
                Ok(SearchEvent::Scanned { count, current }) => {
                    self.scanned = count;
                    self.current = current;
                }
                Ok(SearchEvent::Done { hits, cancelled }) => {
                    self.finished = Some((hits, cancelled));
                    ended = true;
                    break;
                }
                Ok(SearchEvent::Error(e)) => {
                    if self.errors.len() < MAX_ERRORS {
                        self.errors.push(e);
                    }
                }
                Err(TryRecvError::Empty) => break,
                Err(TryRecvError::Disconnected) => {
                    ended = true;
                    break;
                }
            }
        }
        if ended {
            self.run = None;
        } else {
            ctx.request_repaint_after(Duration::from_millis(50));
        }
    }

    fn form_ui(&mut self, ui: &mut egui::Ui) -> bool {
        let mut submit = false;
        let pick = &mut self.root_pick;
        let f = &mut self.form;
        egui::Grid::new("search_form")
            .num_columns(2)
            .spacing([8.0, 4.0])
            .show(ui, |ui| {
                ui.label(t("Search in", "Rechercher dans"));
                let r = folder_field(ui, &mut f.roots, pick, 460.0);
                if r.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) {
                    submit = true;
                }
                ui.end_row();

                ui.label(t("File name", "Nom du fichier"));
                ui.horizontal(|ui| {
                    field(ui, &mut f.name, 300.0, &mut submit);
                    ui.checkbox(&mut f.name_regex, t("Regex", "Regex"));
                });
                ui.end_row();

                ui.label(t("Text to find", "Texte à trouver"));
                field(ui, &mut f.text, 460.0, &mut submit);
                ui.end_row();

                ui.label("");
                ui.horizontal_wrapped(|ui| {
                    ui.checkbox(&mut f.text_regex, t("Regex", "Regex"));
                    ui.checkbox(
                        &mut f.case_sensitive,
                        t("Case sensitive", "Respecter la casse"),
                    );
                    ui.checkbox(&mut f.whole_words, t("Whole words", "Mots entiers"));
                    ui.checkbox(
                        &mut f.not_containing,
                        t("Files NOT containing", "Fichiers NE contenant PAS"),
                    );
                });
                ui.end_row();

                ui.label(t("Depth", "Profondeur"));
                ui.horizontal(|ui| {
                    ui.checkbox(&mut f.depth_limited, t("Limit to", "Limiter à"));
                    ui.add_enabled(
                        f.depth_limited,
                        egui::DragValue::new(&mut f.depth).range(1..=64),
                    );
                    ui.checkbox(
                        &mut f.include_hidden,
                        t("Include hidden", "Fichiers cachés"),
                    );
                    ui.checkbox(&mut f.files, t("Files", "Fichiers"));
                    ui.checkbox(&mut f.dirs, t("Folders", "Dossiers"));
                });
                ui.end_row();

                ui.label(tr("size"));
                ui.horizontal(|ui| {
                    ui.label(t("min", "min"));
                    field(ui, &mut f.min_size, 80.0, &mut submit);
                    ui.label(t("max", "max"));
                    field(ui, &mut f.max_size, 80.0, &mut submit);
                    ui.weak("(10K, 5M, 1G)");
                });
                ui.end_row();

                ui.label(tr("date"));
                ui.horizontal(|ui| {
                    ui.label(t("modified in the last", "modifié depuis"));
                    field(ui, &mut f.newer_days, 50.0, &mut submit);
                    ui.label(t("days, older than", "jours, plus ancien que"));
                    field(ui, &mut f.older_days, 50.0, &mut submit);
                    ui.label(t("days", "jours"));
                });
                ui.end_row();
            });
        submit
    }

    fn status_ui(&self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            if self.run.is_some() {
                ui.spinner();
            }
            ui.label(format!(
                "{}: {}   {}: {}",
                tr("results"),
                grouped(self.hits.len() as u64),
                t("scanned", "analysés"),
                grouped(self.scanned)
            ));
            match self.finished {
                Some((_, true)) => {
                    ui.label(t("(stopped)", "(arrêté)"));
                }
                Some((_, false)) => {
                    ui.label(tr("done"));
                }
                None => {}
            }
        });
        if self.run.is_some() && !self.current.as_os_str().is_empty() {
            ui.add(egui::Label::new(self.current.display().to_string()).truncate());
        }
        for e in &self.errors {
            ui.colored_label(ui.visuals().error_fg_color, e);
        }
    }

    fn table_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        let height = (ui.available_height() - 90.0).max(120.0);
        let mut clicked = None;
        let mut opened = None;
        let selected = self.selected;
        let hits = &self.hits;
        TableBuilder::new(ui)
            .id_salt("search_table")
            .striped(true)
            .resizable(true)
            .sense(egui::Sense::click())
            .max_scroll_height(height)
            .column(Column::initial(380.0).at_least(80.0).clip(true))
            .column(Column::initial(90.0))
            .column(Column::initial(120.0))
            .column(Column::remainder().at_least(60.0).clip(true))
            .header(20.0, |mut h| {
                for title in [tr("path"), tr("size"), tr("date"), t("Line", "Ligne")] {
                    h.col(|ui| {
                        ui.strong(title);
                    });
                }
            })
            .body(|body| {
                body.rows(20.0, hits.len(), |mut row| {
                    let i = row.index();
                    let hit = &hits[i];
                    row.set_selected(selected == Some(i));
                    row.col(|ui| {
                        ui.label(hit.path.display().to_string());
                    });
                    row.col(|ui| {
                        ui.label(if hit.is_dir {
                            String::new()
                        } else {
                            grouped(hit.size)
                        });
                    });
                    row.col(|ui| {
                        ui.label(format_time(hit.modified));
                    });
                    row.col(|ui| {
                        if let Some((n, text)) = &hit.line {
                            ui.label(format!("{n}: {}", text.trim()));
                        }
                    });
                    let resp = row.response();
                    if resp.double_clicked() {
                        opened = Some(i);
                    } else if resp.clicked() {
                        clicked = Some(i);
                    }
                });
            });
        if let Some(i) = clicked {
            self.selected = Some(i);
        }
        if let Some(i) = opened {
            self.selected = Some(i);
            shared.send(AppEvent::GoToFile(self.hits[i].path.clone()));
        }
    }

    fn buttons_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        let running = self.run.is_some();
        let sel = self.selected.and_then(|i| self.hits.get(i)).cloned();
        ui.horizontal(|ui| {
            if running {
                if ui.button(tr("stop")).clicked() {
                    self.stop();
                }
            } else if ui.button(tr("start")).clicked() {
                self.start();
            }
            let can_view = sel.as_ref().is_some_and(|h| !h.is_dir);
            if ui
                .add_enabled(can_view, egui::Button::new(tr("cmd.view")))
                .clicked()
                && let Some(h) = &sel
            {
                shared.send(AppEvent::View(h.path.clone()));
            }
            if ui
                .add_enabled(
                    sel.is_some(),
                    egui::Button::new(t("Go to file", "Aller au fichier")),
                )
                .clicked()
                && let Some(h) = &sel
            {
                shared.send(AppEvent::GoToFile(h.path.clone()));
            }
            let feed = egui::Button::new(t("Feed to panel", "Envoyer au panneau"));
            if ui.add_enabled(!self.hits.is_empty(), feed).clicked() {
                shared.send(AppEvent::FeedToPanel {
                    title: t("Search results", "Résultats de recherche").into(),
                    paths: self.hits.iter().map(|h| h.path.clone()).collect(),
                });
            }
            if ui.button(tr("close")).clicked() {
                self.close = true;
            }
        });
    }
}

/// A single-line field that raises `submit` when Enter is pressed in it.
fn field(ui: &mut egui::Ui, text: &mut String, width: f32, submit: &mut bool) {
    let r = ui.add(egui::TextEdit::singleline(text).desired_width(width));
    if r.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) {
        *submit = true;
    }
}

impl Drop for SearchWindow {
    fn drop(&mut self) {
        self.stop();
    }
}

impl ToolWindow for SearchWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        self.poll(ctx);
        let mut open = true;
        super::tool_window(tr("cmd.search"), ctx, [820.0, 620.0])
            .id(self.id)
            .open(&mut open)
            .resizable(true)
            .show(ctx, |ui| {
                let submit = self.form_ui(ui);
                ui.separator();
                self.status_ui(ui);
                self.table_ui(ui, shared);
                ui.separator();
                self.buttons_ui(ui, shared);
                if submit && self.run.is_none() {
                    self.start();
                }
            });
        open && !self.close
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn form() -> Form {
        Form::new(Path::new("/data"))
    }

    fn now() -> SystemTime {
        SystemTime::UNIX_EPOCH + Duration::from_secs(100 * SECS_PER_DAY)
    }

    #[test]
    fn defaults_make_a_plain_query() {
        let q = build_query(&form(), now()).expect("query");
        assert_eq!(q.roots, vec![PathBuf::from("/data")]);
        assert_eq!(q.name, "*");
        assert!(q.content.is_none());
        assert_eq!(q.max_depth, None);
        assert!(q.min_size.is_none() && q.max_size.is_none());
        assert!(q.newer_than.is_none() && q.older_than.is_none());
        assert!(q.files && q.dirs);
    }

    #[test]
    fn roots_are_split_and_trimmed() {
        let mut f = form();
        f.roots = " /a ; /b;;  ;/c d ".into();
        let q = build_query(&f, now()).expect("query");
        let want: Vec<PathBuf> = ["/a", "/b", "/c d"]
            .into_iter()
            .map(PathBuf::from)
            .collect();
        assert_eq!(q.roots, want);
        f.roots = " ; ".into();
        assert!(build_query(&f, now()).is_err());
    }

    #[test]
    fn sizes_parse_and_empty_means_none() {
        let mut f = form();
        f.min_size = "10K".into();
        f.max_size = " 2M ".into();
        let q = build_query(&f, now()).expect("query");
        assert_eq!(q.min_size, Some(10 * 1024));
        assert_eq!(q.max_size, Some(2 * 1024 * 1024));
        f.min_size = "abc".into();
        assert!(build_query(&f, now()).is_err());
        f.min_size = "3M".into();
        assert!(build_query(&f, now()).is_err(), "min above max");
    }

    #[test]
    fn days_become_system_times() {
        let mut f = form();
        f.newer_days = "7".into();
        f.older_days = "30".into();
        let q = build_query(&f, now()).expect("query");
        let day = Duration::from_secs(SECS_PER_DAY);
        assert_eq!(q.newer_than, Some(now() - day * 7));
        assert_eq!(q.older_than, Some(now() - day * 30));
        f.newer_days = "-1".into();
        assert!(build_query(&f, now()).is_err());
    }

    #[test]
    fn depth_name_and_text() {
        let mut f = form();
        f.depth_limited = true;
        f.depth = 3;
        f.name = "  ".into();
        f.text = "needle".into();
        f.text_regex = true;
        f.not_containing = true;
        let q = build_query(&f, now()).expect("query");
        assert_eq!(q.max_depth, Some(3));
        assert_eq!(q.name, "*");
        assert_eq!(q.content.as_deref(), Some("needle"));
        assert!(q.content_is_regex && q.not_containing);
    }

    #[test]
    fn needs_files_or_folders() {
        let mut f = form();
        f.files = false;
        f.dirs = false;
        assert!(build_query(&f, now()).is_err());
        f.dirs = true;
        assert!(build_query(&f, now()).is_ok());
    }
}
