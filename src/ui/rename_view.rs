//! Multi-rename tool: a form for a `RenameRule`, a live preview of every new name, and
//! apply / undo.

use std::path::PathBuf;

use eframe::egui;
use egui_extras::{Column, TableBuilder};

use super::{AppEvent, Shared, ToolWindow};
use crate::core::multirename::{self, CaseMode, RenamePreview, RenameRule};
use crate::i18n::{t, tr};

/// Placeholder buttons: `(text inserted, English hint, French hint)`.
const TOKENS: &[(&str, &str, &str)] = &[
    ("[N]", "File name without extension", "Nom sans extension"),
    ("[E]", "Extension", "Extension"),
    (
        "[N1-3]",
        "Characters 1 to 3 of the name",
        "Caractères 1 à 3 du nom",
    ),
    (
        "[N2-]",
        "Name from character 2 on",
        "Nom à partir du caractère 2",
    ),
    (
        "[E1-2]",
        "Characters 1 to 2 of the extension",
        "Caractères 1 à 2 de l'extension",
    ),
    (
        "[C]",
        "Counter (see start, step, digits)",
        "Compteur (voir début, pas, chiffres)",
    ),
    (
        "[C1+1:3]",
        "Counter with its own start+step:digits",
        "Compteur début+pas:chiffres",
    ),
    ("[P]", "Parent folder name", "Nom du dossier parent"),
    (
        "[G]",
        "Grandparent folder name",
        "Nom du dossier grand-parent",
    ),
    (
        "[YMD]",
        "Modification date YYYYMMDD",
        "Date de modification AAAAMMJJ",
    ),
    (
        "[hms]",
        "Modification time HHMMSS",
        "Heure de modification HHMMSS",
    ),
    ("[Y]", "Year", "Année"),
    ("[M]", "Month", "Mois"),
    ("[D]", "Day", "Jour"),
    ("[h]", "Hour", "Heure"),
    ("[m]", "Minute", "Minute"),
    ("[s]", "Second", "Seconde"),
];

const CASE_MODES: [CaseMode; 5] = [
    CaseMode::Unchanged,
    CaseMode::Lower,
    CaseMode::Upper,
    CaseMode::FirstUpper,
    CaseMode::TitleCase,
];

fn case_label(mode: CaseMode) -> &'static str {
    match mode {
        CaseMode::Unchanged => t("Unchanged", "Inchangé"),
        CaseMode::Lower => t("lower case", "minuscules"),
        CaseMode::Upper => t("UPPER CASE", "MAJUSCULES"),
        CaseMode::FirstUpper => t("First letter upper", "Première lettre en majuscule"),
        CaseMode::TitleCase => t("Title Case", "Chaque Mot En Majuscule"),
    }
}

/// Replaces the chars `start..end` of `text` (a cursor, when equal) by `token`; returns the
/// new text and the char index just after the token. Out-of-range indices are clamped.
fn insert_at(text: &str, start: usize, end: usize, token: &str) -> (String, usize) {
    let len = text.chars().count();
    let (a, b) = (start.min(end).min(len), start.max(end).min(len));
    let byte = |n: usize| text.char_indices().nth(n).map_or(text.len(), |(i, _)| i);
    let mut out = String::with_capacity(text.len() + token.len());
    out.push_str(&text[..byte(a)]);
    out.push_str(token);
    out.push_str(&text[byte(b)..]);
    (out, a + token.chars().count())
}

/// Follows the files through a rename (`forward`) or its undo: a path that was moved by one of
/// the `done` pairs becomes its counterpart.
fn remap_paths(files: &[PathBuf], done: &[(PathBuf, PathBuf)], forward: bool) -> Vec<PathBuf> {
    files
        .iter()
        .map(|f| {
            done.iter()
                .find_map(|(from, to)| match forward {
                    true => (from == f).then(|| to.clone()),
                    false => (to == f).then(|| from.clone()),
                })
                .unwrap_or_else(|| f.clone())
        })
        .collect()
}

fn file_label(p: &std::path::Path) -> String {
    p.file_name().map_or_else(
        || p.display().to_string(),
        |n| n.to_string_lossy().into_owned(),
    )
}

pub struct RenameWindow {
    id: egui::Id,
    mask_id: egui::Id,
    files: Vec<PathBuf>,
    rule: RenameRule,
    previews: Vec<RenamePreview>,
    /// Pairs renamed by the last Start, for Undo.
    done: Vec<(PathBuf, PathBuf)>,
    message: Option<Result<String, String>>,
    close: bool,
}

impl RenameWindow {
    pub fn new(files: Vec<PathBuf>) -> Self {
        let rule = RenameRule::default();
        let previews = multirename::preview(&files, &rule);
        let id = super::window_id("RenameWindow");
        Self {
            id,
            mask_id: id.with("mask"),
            files,
            rule,
            previews,
            done: Vec::new(),
            message: None,
            close: false,
        }
    }

    fn refresh(&mut self) {
        self.previews = multirename::preview(&self.files, &self.rule);
    }

    fn has_error(&self) -> bool {
        self.previews.iter().any(|p| p.error.is_some())
    }

    fn start(&mut self, shared: &Shared) {
        match multirename::apply(&self.previews) {
            Ok(done) => {
                self.files = remap_paths(&self.files, &done, true);
                self.message = Some(Ok(format!("{} {}", done.len(), t("renamed", "renommé(s)"))));
                self.done = done;
                self.refresh();
                shared.send(AppEvent::Reload);
            }
            Err(e) => self.message = Some(Err(e)),
        }
    }

    fn undo(&mut self, shared: &Shared) {
        match multirename::undo(&self.done) {
            Ok(()) => {
                self.files = remap_paths(&self.files, &self.done, false);
                self.message = Some(Ok(format!(
                    "{} {}",
                    self.done.len(),
                    t("restored", "restauré(s)")
                )));
                self.done.clear();
                self.refresh();
                shared.send(AppEvent::Reload);
            }
            Err(e) => self.message = Some(Err(e)),
        }
    }

    fn insert_token(&mut self, ctx: &egui::Context, token: &str) {
        let mut state = egui::TextEdit::load_state(ctx, self.mask_id).unwrap_or_default();
        let len = self.rule.name_mask.chars().count();
        let (start, end) = state
            .cursor
            .char_range()
            .map_or((len, len), |r| (r.primary.index.0, r.secondary.index.0));
        let (text, pos) = insert_at(&self.rule.name_mask, start, end, token);
        self.rule.name_mask = text;
        let caret = egui::text::CCursor::new(pos);
        state
            .cursor
            .set_char_range(Some(egui::text::CCursorRange::one(caret)));
        egui::TextEdit::store_state(ctx, self.mask_id, state);
        ctx.memory_mut(|m| m.request_focus(self.mask_id));
    }

    fn form_ui(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) -> bool {
        let mut changed = false;
        let mut token = None;
        egui::Grid::new("rename_form")
            .num_columns(2)
            .spacing([8.0, 4.0])
            .show(ui, |ui| {
                ui.label(t("Name mask", "Masque du nom"));
                let edit = egui::TextEdit::singleline(&mut self.rule.name_mask)
                    .id(self.mask_id)
                    .desired_width(380.0);
                changed |= ui.add(edit).changed();
                ui.end_row();

                ui.label("");
                ui.horizontal_wrapped(|ui| {
                    for (tok, en, fr) in TOKENS {
                        let hint = if crate::i18n::lang() == crate::i18n::Lang::Fr {
                            fr
                        } else {
                            en
                        };
                        if ui.small_button(*tok).on_hover_text(*hint).clicked() {
                            token = Some(*tok);
                        }
                    }
                });
                ui.end_row();

                ui.label(t("Extension mask", "Masque de l'extension"));
                let edit = egui::TextEdit::singleline(&mut self.rule.ext_mask).desired_width(160.0);
                changed |= ui.add(edit).changed();
                ui.end_row();

                ui.label(t("Search", "Rechercher"));
                let edit = egui::TextEdit::singleline(&mut self.rule.search).desired_width(380.0);
                changed |= ui.add(edit).changed();
                ui.end_row();

                ui.label(t("Replace", "Remplacer"));
                let edit = egui::TextEdit::singleline(&mut self.rule.replace).desired_width(380.0);
                changed |= ui.add(edit).changed();
                ui.end_row();

                ui.label("");
                ui.horizontal(|ui| {
                    changed |= ui
                        .checkbox(&mut self.rule.use_regex, t("Regex", "Regex"))
                        .changed();
                    changed |= ui
                        .checkbox(
                            &mut self.rule.case_sensitive,
                            t("Case sensitive", "Respecter la casse"),
                        )
                        .changed();
                });
                ui.end_row();

                ui.label(t("Case", "Casse"));
                changed |= case_combo(ui, &mut self.rule.case_mode);
                ui.end_row();

                ui.label(t("Counter", "Compteur"));
                ui.horizontal(|ui| changed |= counter_ui(ui, &mut self.rule));
                ui.end_row();
            });
        if let Some(tok) = token {
            self.insert_token(ctx, tok);
            changed = true;
        }
        changed
    }

    fn table_ui(&self, ui: &mut egui::Ui) {
        let height = (ui.available_height() - 70.0).max(100.0);
        let previews = &self.previews;
        TableBuilder::new(ui)
            .id_salt("rename_table")
            .striped(true)
            .resizable(true)
            .max_scroll_height(height)
            .column(Column::initial(260.0).at_least(80.0).clip(true))
            .column(Column::initial(260.0).at_least(80.0).clip(true))
            .column(Column::remainder().at_least(40.0).clip(true))
            .header(20.0, |mut h| {
                for title in [
                    t("Old name", "Ancien nom"),
                    t("New name", "Nouveau nom"),
                    tr("error"),
                ] {
                    h.col(|ui| {
                        ui.strong(title);
                    });
                }
            })
            .body(|body| {
                body.rows(20.0, previews.len(), |mut row| {
                    let p = &previews[row.index()];
                    let red = p.error.is_some();
                    row.col(|ui| {
                        ui.label(file_label(&p.from));
                    });
                    row.col(|ui| {
                        let text = file_label(&p.to);
                        if red {
                            ui.colored_label(ui.visuals().error_fg_color, text);
                        } else if p.to == p.from {
                            ui.weak(text);
                        } else {
                            ui.label(text);
                        }
                    });
                    row.col(|ui| {
                        if let Some(e) = &p.error {
                            ui.colored_label(ui.visuals().error_fg_color, e);
                        }
                    });
                });
            });
    }

    fn buttons_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        ui.horizontal(|ui| {
            let can_start = !self.previews.is_empty() && !self.has_error();
            if ui
                .add_enabled(can_start, egui::Button::new(tr("start")))
                .clicked()
            {
                self.start(shared);
            }
            if ui
                .add_enabled(
                    !self.done.is_empty(),
                    egui::Button::new(t("Undo", "Annuler")),
                )
                .clicked()
            {
                self.undo(shared);
            }
            if ui.button(tr("close")).clicked() {
                self.close = true;
            }
            match &self.message {
                Some(Ok(m)) => {
                    ui.label(m);
                }
                Some(Err(e)) => {
                    ui.colored_label(ui.visuals().error_fg_color, e);
                }
                None => {}
            }
        });
    }
}

fn case_combo(ui: &mut egui::Ui, mode: &mut CaseMode) -> bool {
    let before = *mode;
    egui::ComboBox::from_id_salt("rename_case")
        .selected_text(case_label(*mode))
        .show_ui(ui, |ui| {
            for m in CASE_MODES {
                ui.selectable_value(mode, m, case_label(m));
            }
        });
    before != *mode
}

fn counter_ui(ui: &mut egui::Ui, rule: &mut RenameRule) -> bool {
    let mut changed = false;
    ui.label(t("start", "début"));
    changed |= ui
        .add(egui::DragValue::new(&mut rule.counter_start))
        .changed();
    ui.label(t("step", "pas"));
    changed |= ui
        .add(egui::DragValue::new(&mut rule.counter_step))
        .changed();
    ui.label(t("digits", "chiffres"));
    let digits = egui::DragValue::new(&mut rule.counter_digits).range(1..=12);
    changed |= ui.add(digits).changed();
    changed
}

impl ToolWindow for RenameWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        let mut open = true;
        super::tool_window(tr("cmd.multirename"), ctx, [760.0, 600.0])
            .id(self.id)
            .open(&mut open)
            .resizable(true)
            .show(ctx, |ui| {
                if self.form_ui(ui, ctx) {
                    self.refresh();
                    self.message = None;
                }
                ui.separator();
                self.table_ui(ui);
                ui.separator();
                self.buttons_ui(ui, shared);
            });
        open && !self.close
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn insert_at_cursor_and_selection() {
        assert_eq!(insert_at("abc", 1, 1, "[N]"), ("a[N]bc".into(), 4));
        assert_eq!(insert_at("abc", 0, 3, "X"), ("X".into(), 1));
        assert_eq!(insert_at("abc", 3, 1, "X"), ("aX".into(), 2));
        assert_eq!(insert_at("abc", 99, 99, "Z"), ("abcZ".into(), 4));
        // Indices count chars, not bytes.
        assert_eq!(insert_at("é€x", 2, 2, "-"), ("é€-x".into(), 3));
    }

    #[test]
    fn remap_follows_a_rename_and_its_undo() {
        let (a, b, c) = (
            PathBuf::from("/d/a"),
            PathBuf::from("/d/b"),
            PathBuf::from("/d/c"),
        );
        let done = vec![(a.clone(), b.clone())];
        let files = vec![a.clone(), c.clone()];
        let after = remap_paths(&files, &done, true);
        assert_eq!(after, vec![b, c.clone()]);
        assert_eq!(remap_paths(&after, &done, false), vec![a, c]);
    }

    #[test]
    fn every_token_button_expands() {
        let dir = tempfile::tempdir().expect("tempdir");
        let file = dir.path().join("report.txt");
        std::fs::write(&file, "x").expect("write");
        for (tok, _, _) in TOKENS {
            let rule = RenameRule {
                name_mask: (*tok).into(),
                ..RenameRule::default()
            };
            let name = multirename::new_name(&file, 0, &rule);
            assert!(name.is_ok(), "{tok}: {name:?}");
        }
    }

    #[test]
    fn start_then_undo_round_trip() {
        let dir = tempfile::tempdir().expect("tempdir");
        let files: Vec<PathBuf> = ["a.txt", "b.txt"]
            .iter()
            .map(|n| {
                let p = dir.path().join(n);
                std::fs::write(&p, "x").expect("write");
                p
            })
            .collect();
        let (tx, _rx) = std::sync::mpsc::channel();
        let shared = test_shared(tx);
        let mut w = RenameWindow::new(files);
        w.rule.name_mask = "n[C]".into();
        w.refresh();
        assert!(!w.has_error());
        w.start(&shared);
        assert!(dir.path().join("n1.txt").exists() && dir.path().join("n2.txt").exists());
        assert!(w.files.iter().all(|f| f.exists()));
        w.undo(&shared);
        assert!(dir.path().join("a.txt").exists() && dir.path().join("b.txt").exists());
        assert!(w.done.is_empty());
    }

    #[test]
    fn preview_error_blocks_start() {
        let dir = tempfile::tempdir().expect("tempdir");
        let files: Vec<PathBuf> = ["a.txt", "b.txt"]
            .iter()
            .map(|n| {
                let p = dir.path().join(n);
                std::fs::write(&p, "x").expect("write");
                p
            })
            .collect();
        let mut w = RenameWindow::new(files);
        w.rule.name_mask = "same".into();
        w.refresh();
        assert!(w.has_error(), "two files cannot share one name");
    }

    fn test_shared(tx: std::sync::mpsc::Sender<AppEvent>) -> Shared {
        Shared {
            settings: crate::config::Settings::default(),
            passwords: None,
            tx,
            active_dir: None,
            other_dir: None,
            active_selection: Vec::new(),
        }
    }
}
