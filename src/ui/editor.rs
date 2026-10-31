//! Built-in text editor (F4 when no external editor is configured).

mod ops;

use std::ops::Range;
use std::path::PathBuf;

use eframe::egui::{self, Key, KeyboardShortcut, Modifiers, ScrollArea, TextEdit};

use super::{AppEvent, Shared, ToolWindow};
use crate::core::launcher::open_with_default;
use crate::i18n::{t, tr};
use ops::LineEnding;

pub(super) use ops::{LineEnding as Eol, decode_file, encode_file, replace_all, write_atomic};

/// Larger files are not loaded into the editor.
const MAX_EDIT_SIZE: u64 = 8 * 1024 * 1024;

enum CloseChoice {
    Pending,
    Save,
    Discard,
    Cancel,
}

pub struct EditorWindow {
    id: egui::Id,
    edit_id: egui::Id,
    confirm_id: egui::Id,
    path: PathBuf,
    name: String,
    /// LF-only editing buffer.
    text: String,
    eol: LineEnding,
    bom: bool,
    lossy: bool,
    dirty: bool,
    /// Why the file was not loaded (too big, unreadable).
    refused: Option<String>,
    too_big: bool,
    find: String,
    replace: String,
    case: bool,
    status: String,
    status_is_error: bool,
    confirm_close: bool,
    /// Byte range to select (and scroll to) before the next frame.
    pending_select: Option<Range<usize>>,
}

impl EditorWindow {
    /// Loads `path` when it exists, else starts empty (saving creates it).
    pub fn new(path: PathBuf) -> Self {
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| path.display().to_string());
        let mut w = Self {
            id: super::window_id("editor"),
            edit_id: super::window_id("editor-text"),
            confirm_id: super::window_id("editor-confirm"),
            path,
            name,
            text: String::new(),
            eol: LineEnding::Lf,
            bom: false,
            lossy: false,
            dirty: false,
            refused: None,
            too_big: false,
            find: String::new(),
            replace: String::new(),
            case: false,
            status: String::new(),
            status_is_error: false,
            confirm_close: false,
            pending_select: None,
        };
        w.load();
        w
    }

    fn load(&mut self) {
        match std::fs::metadata(&self.path) {
            Ok(m) if m.len() > MAX_EDIT_SIZE => {
                self.too_big = true;
                self.refused = Some(
                    t(
                        "This file is larger than 8 MiB: it is not opened in the built-in editor.",
                        "Ce fichier dépasse 8 Mio : il n'est pas ouvert dans l'éditeur intégré.",
                    )
                    .into(),
                );
            }
            Ok(_) => match std::fs::read(&self.path) {
                Ok(bytes) => {
                    let d = decode_file(&bytes);
                    self.text = d.text;
                    self.eol = d.eol;
                    self.bom = d.bom;
                    self.lossy = d.lossy;
                }
                Err(e) => self.refused = Some(format!("{}: {e}", tr("error"))),
            },
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => self.refused = Some(format!("{}: {e}", tr("error"))),
        }
    }

    fn set_status(&mut self, msg: String, error: bool) {
        self.status = msg;
        self.status_is_error = error;
    }

    /// Writes the buffer back (atomically, with its original line endings).
    fn save(&mut self, shared: &Shared) -> bool {
        let bytes = encode_file(&self.text, self.eol, self.bom);
        match write_atomic(&self.path, &bytes) {
            Ok(()) => {
                self.dirty = false;
                self.set_status(t("Saved", "Enregistré").into(), false);
                shared.send(AppEvent::Reload);
                true
            }
            Err(e) => {
                self.set_status(format!("{}: {e}", tr("error")), true);
                false
            }
        }
    }

    fn title(&self) -> String {
        format!(
            "{}{} — {}",
            if self.dirty { "*" } else { "" },
            self.name,
            tr("cmd.edit")
        )
    }

    fn char_to_byte(&self, c: usize) -> usize {
        self.text
            .char_indices()
            .nth(c)
            .map_or(self.text.len(), |(i, _)| i)
    }

    fn byte_to_char(&self, b: usize) -> usize {
        self.text[..b.min(self.text.len())].chars().count()
    }

    /// The current selection of the text edit as a byte range.
    fn selection(&self, ctx: &egui::Context) -> Option<Range<usize>> {
        let range = TextEdit::load_state(ctx, self.edit_id)?
            .cursor
            .char_range()?;
        let r = range.as_sorted_char_range();
        Some(self.char_to_byte(r.start.into())..self.char_to_byte(r.end.into()))
    }

    fn find_next(&mut self, ctx: &egui::Context) {
        let from = self.selection(ctx).map_or(0, |r| r.end);
        match ops::find_next(&self.text, &self.find, self.case, from) {
            Some(r) => self.pending_select = Some(r),
            None => self.set_status(t("Not found", "Introuvable").into(), true),
        }
    }

    fn replace_one(&mut self, ctx: &egui::Context) {
        if let Some(sel) = self.selection(ctx)
            && ops::match_len_at(&self.text[sel.start..], &self.find, self.case) == Some(sel.len())
        {
            self.text.replace_range(sel.clone(), &self.replace);
            self.dirty = true;
            let after = sel.start + self.replace.len();
            self.pending_select = Some(after..after);
            // Continue from the end of the replacement.
            match ops::find_next(&self.text, &self.find, self.case, after) {
                Some(r) => self.pending_select = Some(r),
                None => self.set_status(t("Not found", "Introuvable").into(), true),
            }
        } else {
            self.find_next(ctx);
        }
    }

    fn replace_everywhere(&mut self) {
        let (text, n) = replace_all(&self.text, &self.find, &self.replace, self.case);
        if n > 0 {
            self.text = text;
            self.dirty = true;
        }
        self.set_status(format!("{n} {}", t("replaced", "remplacé(s)")), false);
    }

    fn apply_pending_select(&mut self, ctx: &egui::Context) {
        let Some(r) = self.pending_select.take() else {
            return;
        };
        let (a, b) = (self.byte_to_char(r.start), self.byte_to_char(r.end));
        let mut state = TextEdit::load_state(ctx, self.edit_id).unwrap_or_default();
        state
            .cursor
            .set_char_range(Some(egui::text::CCursorRange::two(
                egui::text::CCursor::new(a),
                egui::text::CCursor::new(b),
            )));
        state.store(ctx, self.edit_id);
        ctx.memory_mut(|m| m.request_focus(self.edit_id));
    }

    fn toolbar(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        ui.horizontal_wrapped(|ui| {
            if ui.button(tr("save")).clicked() {
                self.save(shared);
            }
            ui.weak(format!(
                "{} · {}{}",
                self.path.display(),
                self.eol.label(),
                if self.bom { " · BOM" } else { "" }
            ));
            if self.lossy {
                ui.colored_label(
                    ui.visuals().warn_fg_color,
                    t(
                        "Invalid UTF-8 was replaced; saving rewrites those bytes.",
                        "UTF-8 invalide remplacé ; l'enregistrement réécrit ces octets.",
                    ),
                );
            }
            if !self.status.is_empty() {
                let color = if self.status_is_error {
                    ui.visuals().error_fg_color
                } else {
                    ui.visuals().weak_text_color()
                };
                ui.colored_label(color, &self.status);
            }
        });
        ui.horizontal_wrapped(|ui| {
            ui.label(t("Find", "Chercher"));
            let r = ui.add(TextEdit::singleline(&mut self.find).desired_width(160.0));
            let enter = r.lost_focus() && ui.input(|i| i.key_pressed(Key::Enter));
            ui.label(t("Replace", "Remplacer"));
            ui.add(TextEdit::singleline(&mut self.replace).desired_width(160.0));
            ui.checkbox(&mut self.case, t("Match case", "Respecter la casse"));
            let ctx = ui.ctx().clone();
            if ui.button(t("Find next", "Suivant")).clicked() || enter {
                self.find_next(&ctx);
            }
            if ui.button(t("Replace", "Remplacer")).clicked() {
                self.replace_one(&ctx);
            }
            if ui.button(t("Replace all", "Tout remplacer")).clicked() {
                self.replace_everywhere();
            }
        });
    }

    fn editor_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        self.apply_pending_select(ui.ctx());
        let row_h = ui.text_style_height(&egui::TextStyle::Monospace);
        let rows = ((ui.available_height() / row_h) as usize).max(4);
        let out = ScrollArea::both()
            .id_salt(self.edit_id.with("scroll"))
            .auto_shrink(false)
            .show(ui, |ui| {
                TextEdit::multiline(&mut self.text)
                    .id(self.edit_id)
                    .code_editor()
                    .desired_width(f32::INFINITY)
                    .desired_rows(rows)
                    .show(ui)
            })
            .inner;
        if out.response.changed() {
            self.dirty = true;
            self.status.clear();
        }
        let ctrl_s = KeyboardShortcut::new(Modifiers::COMMAND, Key::S);
        if out.response.has_focus() && ui.input_mut(|i| i.consume_shortcut(&ctrl_s)) {
            self.save(shared);
        }
    }

    fn refused_ui(&mut self, ui: &mut egui::Ui, msg: &str) {
        ui.label(msg);
        ui.weak(self.path.display().to_string());
        if self.too_big
            && ui
                .button(t(
                    "Open with default app",
                    "Ouvrir avec l'application par défaut",
                ))
                .clicked()
            && let Err(e) = open_with_default(&self.path)
        {
            self.set_status(e, true);
        }
        if self.status_is_error && !self.status.is_empty() {
            ui.colored_label(ui.visuals().error_fg_color, &self.status);
        }
    }

    fn confirm_ui(&mut self, ctx: &egui::Context) -> CloseChoice {
        let mut choice = CloseChoice::Pending;
        egui::Window::new(t("Unsaved changes", "Modifications non enregistrées"))
            .id(self.confirm_id)
            .collapsible(false)
            .resizable(false)
            .anchor(egui::Align2::CENTER_CENTER, [0.0, 0.0])
            .show(ctx, |ui| {
                ui.label(format!(
                    "{} {}?",
                    t("Save changes to", "Enregistrer les modifications de"),
                    self.name
                ));
                ui.horizontal(|ui| {
                    if ui.button(tr("save")).clicked() {
                        choice = CloseChoice::Save;
                    }
                    if ui.button(t("Discard", "Ne pas enregistrer")).clicked() {
                        choice = CloseChoice::Discard;
                    }
                    if ui.button(tr("cancel")).clicked() {
                        choice = CloseChoice::Cancel;
                    }
                });
            });
        choice
    }
}

impl ToolWindow for EditorWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        let mut open = true;
        egui::Window::new(self.title())
            .id(self.id)
            .open(&mut open)
            .default_size([820.0, 620.0])
            .show(ctx, |ui| match self.refused.clone() {
                Some(msg) => self.refused_ui(ui, &msg),
                None => {
                    self.toolbar(ui, shared);
                    ui.separator();
                    self.editor_ui(ui, shared);
                }
            });
        if !open {
            if !self.dirty {
                return false;
            }
            self.confirm_close = true;
        }
        if self.confirm_close {
            match self.confirm_ui(ctx) {
                CloseChoice::Pending => {}
                CloseChoice::Save => {
                    if self.save(shared) {
                        return false;
                    }
                    self.confirm_close = false;
                }
                CloseChoice::Discard => return false,
                CloseChoice::Cancel => self.confirm_close = false,
            }
        }
        true
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn new_editor_loads_and_keeps_crlf_on_save() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("a.txt");
        std::fs::write(&p, "x\r\ny\r\n").unwrap();
        let mut w = EditorWindow::new(p.clone());
        assert_eq!(w.text, "x\ny\n");
        assert_eq!(w.eol, LineEnding::Crlf);
        w.text.push_str("z\n");
        let (tx, rx) = std::sync::mpsc::channel();
        let shared = test_shared(tx);
        assert!(w.save(&shared));
        assert_eq!(std::fs::read(&p).unwrap(), b"x\r\ny\r\nz\r\n");
        assert!(matches!(rx.try_recv(), Ok(AppEvent::Reload)));
        assert!(!w.dirty);
    }

    #[test]
    fn missing_file_starts_empty_and_save_creates_it() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("new.txt");
        let mut w = EditorWindow::new(p.clone());
        assert!(w.refused.is_none() && w.text.is_empty());
        w.text = "hi\n".into();
        let (tx, _rx) = std::sync::mpsc::channel();
        assert!(w.save(&test_shared(tx)));
        assert_eq!(std::fs::read(&p).unwrap(), b"hi\n");
    }

    #[test]
    fn oversized_file_is_refused() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("big.bin");
        let f = std::fs::File::create(&p).unwrap();
        f.set_len(MAX_EDIT_SIZE + 1).unwrap();
        let w = EditorWindow::new(p);
        assert!(w.too_big && w.refused.is_some());
    }

    #[test]
    fn replace_everywhere_marks_dirty() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("r.txt");
        std::fs::write(&p, "aa Aa").unwrap();
        let mut w = EditorWindow::new(p);
        w.find = "aa".into();
        w.replace = "b".into();
        w.replace_everywhere();
        assert_eq!(w.text, "b b");
        assert!(w.dirty);
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

#[cfg(test)]
mod render_tests {
    use super::*;

    #[test]
    fn window_renders_and_find_replace_select() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("e.txt");
        std::fs::write(&p, "alpha beta\nBETA gamma\n").unwrap();
        let mut w = EditorWindow::new(p);
        let (tx, _rx) = std::sync::mpsc::channel();
        let mut shared = Shared {
            settings: crate::config::Settings::default(),
            passwords: None,
            tx,
            active_dir: None,
            other_dir: None,
            active_selection: Vec::new(),
        };
        let ctx = egui::Context::default();
        let mut pass = |w: &mut EditorWindow| {
            ctx.run_pass(egui::RawInput::default(), |c| {
                assert!(w.show(c, &mut shared));
            })
            .drop_without_applying_deltas();
        };
        pass(&mut w);
        w.find = "beta".into();
        w.replace = "X".into();
        w.find_next(&ctx);
        assert_eq!(w.pending_select, Some(6..10));
        pass(&mut w);
        w.replace_one(&ctx);
        assert_eq!(w.text, "alpha X\nBETA gamma\n");
        assert!(w.dirty);
        pass(&mut w);
    }
}
