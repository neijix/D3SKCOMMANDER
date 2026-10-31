//! Compare by content (side-by-side diff editor).

mod diff_view;

use std::ops::Range;
use std::path::PathBuf;
use std::sync::mpsc::{Receiver, TryRecvError, channel};

use eframe::egui::{self, ScrollArea, TextEdit};

use super::editor::{Eol, decode_file, encode_file, write_atomic};
use super::{AppEvent, Shared, ToolWindow};
use crate::core::compare::{
    DiffOptions, DiffRow, FileDiff, diff_files, diff_text, next_difference, prev_difference,
};
use crate::i18n::{t, tr};
use diff_view::Peek;

/// Files above this size are not loaded into the compare editor.
const MAX_EDIT_SIZE: u64 = 4 * 1024 * 1024;

enum Outcome {
    None,
    Text {
        rows: Vec<DiffRow>,
        blocks: usize,
    },
    Binary {
        identical: bool,
        first: Option<u64>,
        peek: Option<Peek>,
    },
    Error(String),
}

type JobResult = Result<Outcome, String>;

#[derive(Clone, Copy, PartialEq, Eq)]
enum Mode {
    View,
    Edit,
}

/// One side of the compare editor.
struct Side {
    path: PathBuf,
    text: String,
    eol: Eol,
    bom: bool,
    dirty: bool,
}

impl Side {
    fn load(path: PathBuf) -> Result<Side, String> {
        let meta = std::fs::metadata(&path).map_err(|e| format!("{}: {e}", path.display()))?;
        if meta.len() > MAX_EDIT_SIZE {
            return Err(format!(
                "{}: {}",
                path.display(),
                t(
                    "too large to edit (over 4 MiB)",
                    "trop gros pour être édité (plus de 4 Mio)"
                )
            ));
        }
        let bytes = std::fs::read(&path).map_err(|e| format!("{}: {e}", path.display()))?;
        let d = decode_file(&bytes);
        Ok(Side {
            path,
            text: d.text,
            eol: d.eol,
            bom: d.bom,
            dirty: false,
        })
    }

    fn save(&mut self) -> std::io::Result<()> {
        write_atomic(&self.path, &encode_file(&self.text, self.eol, self.bom))?;
        self.dirty = false;
        Ok(())
    }
}

pub struct CompareWindow {
    id: egui::Id,
    left: String,
    right: String,
    opts: DiffOptions,
    outcome: Outcome,
    rx: Option<Receiver<JobResult>>,
    started: bool,
    mode: Mode,
    current: Option<usize>,
    scroll_to: Option<usize>,
    edit: Option<[Side; 2]>,
    status: String,
}

impl CompareWindow {
    pub fn new(left: PathBuf, right: PathBuf) -> Self {
        Self {
            id: super::window_id("compare"),
            left: left.display().to_string(),
            right: right.display().to_string(),
            opts: DiffOptions::default(),
            outcome: Outcome::None,
            rx: None,
            started: false,
            mode: Mode::View,
            current: None,
            scroll_to: None,
            edit: None,
            status: String::new(),
        }
    }

    fn spawn(&mut self, ctx: &egui::Context, job: impl FnOnce() -> JobResult + Send + 'static) {
        let (tx, rx) = channel();
        let ctx = ctx.clone();
        std::thread::spawn(move || {
            let _ = tx.send(job());
            ctx.request_repaint();
        });
        // A previous receiver is dropped: its thread's result is ignored.
        self.rx = Some(rx);
    }

    /// Compares the two files named in the path boxes, on a background thread.
    fn compare_files(&mut self, ctx: &egui::Context) {
        let (left, right) = (
            PathBuf::from(self.left.trim()),
            PathBuf::from(self.right.trim()),
        );
        let opts = self.opts;
        self.edit = None;
        self.mode = Mode::View;
        self.status.clear();
        self.spawn(ctx, move || {
            let diff = diff_files(&left, &right, &opts).map_err(|e| e.to_string())?;
            Ok(match diff {
                FileDiff::Text(rows) => {
                    let blocks = diff_view::block_count(&rows);
                    Outcome::Text { rows, blocks }
                }
                FileDiff::Binary {
                    identical,
                    first_difference,
                } => {
                    let peek = match first_difference {
                        Some(off) => diff_view::read_peek(&left, &right, off).ok(),
                        None => None,
                    };
                    Outcome::Binary {
                        identical,
                        first: first_difference,
                        peek,
                    }
                }
            })
        });
    }

    /// Diffs the edit buffers (not the files), on a background thread.
    fn recompare_buffers(&mut self, ctx: &egui::Context) {
        let Some([l, r]) = &self.edit else {
            return;
        };
        let (a, b, opts) = (l.text.clone(), r.text.clone(), self.opts);
        self.spawn(ctx, move || {
            let rows = diff_text(&a, &b, &opts);
            let blocks = diff_view::block_count(&rows);
            Ok(Outcome::Text { rows, blocks })
        });
    }

    fn rerun(&mut self, ctx: &egui::Context) {
        if self.mode == Mode::Edit && self.edit.is_some() {
            self.recompare_buffers(ctx);
        } else {
            self.compare_files(ctx);
        }
    }

    fn poll(&mut self) {
        let Some(rx) = &self.rx else {
            return;
        };
        let result = match rx.try_recv() {
            Ok(r) => r,
            Err(TryRecvError::Empty) => return,
            Err(TryRecvError::Disconnected) => Err("comparison failed".into()),
        };
        self.rx = None;
        self.current = None;
        self.scroll_to = None;
        self.outcome = result.unwrap_or_else(Outcome::Error);
    }

    fn enter_edit(&mut self) {
        if self.edit.is_none() {
            let loaded = Side::load(PathBuf::from(self.left.trim()))
                .and_then(|l| Side::load(PathBuf::from(self.right.trim())).map(|r| [l, r]));
            match loaded {
                Ok(sides) => self.edit = Some(sides),
                Err(e) => {
                    self.status = e;
                    return;
                }
            }
        }
        self.mode = Mode::Edit;
    }

    fn save_side(&mut self, idx: usize, shared: &Shared) {
        let Some(sides) = &mut self.edit else {
            return;
        };
        match sides[idx].save() {
            Ok(()) => {
                self.status = format!("{} {}", t("Saved", "Enregistré"), sides[idx].path.display());
                shared.send(AppEvent::Reload);
            }
            Err(e) => self.status = format!("{}: {e}", tr("error")),
        }
    }

    fn goto_difference(&mut self, forward: bool) {
        let Outcome::Text { rows, .. } = &self.outcome else {
            return;
        };
        let target = match (self.current, forward) {
            (None, true)
                if rows
                    .first()
                    .is_some_and(|r| r.kind != crate::core::compare::RowKind::Equal) =>
            {
                Some(0)
            }
            (None, true) => next_difference(rows, 0),
            (None, false) => prev_difference(rows, rows.len()),
            (Some(c), true) => next_difference(rows, c),
            (Some(c), false) => prev_difference(rows, c),
        };
        if let Some(row) = target {
            self.current = Some(row);
            self.scroll_to = Some(row);
        }
    }

    fn toolbar(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        let ctx = ui.ctx().clone();
        ui.horizontal(|ui| {
            let w = (ui.available_width() - 180.0).max(200.0) / 2.0;
            ui.add(TextEdit::singleline(&mut self.left).desired_width(w));
            if ui
                .button("⇄")
                .on_hover_text(t("Swap", "Échanger"))
                .clicked()
            {
                std::mem::swap(&mut self.left, &mut self.right);
                self.edit = None;
                self.compare_files(&ctx);
            }
            ui.add(TextEdit::singleline(&mut self.right).desired_width(w));
            if ui.button(t("Compare", "Comparer")).clicked() {
                self.compare_files(&ctx);
            }
        });
        ui.horizontal_wrapped(|ui| {
            let is_text = matches!(self.outcome, Outcome::Text { .. });
            if is_text && ui.button(t("Previous", "Précédent")).clicked() {
                self.goto_difference(false);
            }
            if is_text && ui.button(t("Next", "Suivant")).clicked() {
                self.goto_difference(true);
            }
            if let Outcome::Text { blocks, .. } = &self.outcome {
                ui.label(format!("{blocks} {}", t("difference(s)", "différence(s)")));
            }
            let mut changed = ui
                .checkbox(
                    &mut self.opts.ignore_case,
                    t("Ignore case", "Ignorer la casse"),
                )
                .changed();
            changed |= ui
                .checkbox(
                    &mut self.opts.ignore_whitespace,
                    t("Ignore whitespace", "Ignorer les espaces"),
                )
                .changed();
            if changed {
                self.rerun(&ctx);
            }
            if is_text || self.edit.is_some() {
                ui.separator();
                ui.selectable_value(
                    &mut self.mode,
                    Mode::View,
                    t("Compare view", "Vue comparaison"),
                );
                if ui
                    .selectable_label(self.mode == Mode::Edit, tr("cmd.edit"))
                    .clicked()
                {
                    self.enter_edit();
                }
            }
            if self.mode == Mode::Edit && self.edit.is_some() {
                self.edit_buttons(ui, shared, &ctx);
            }
            if self.rx.is_some() {
                ui.spinner();
            }
        });
        if !self.status.is_empty() {
            ui.label(&self.status);
        }
    }

    fn edit_buttons(&mut self, ui: &mut egui::Ui, shared: &Shared, ctx: &egui::Context) {
        if ui.button(t("Recompare", "Recomparer")).clicked() {
            self.recompare_buffers(ctx);
        }
        if ui.button(t("Save left", "Enregistrer à gauche")).clicked() {
            self.save_side(0, shared);
        }
        if ui.button(t("Save right", "Enregistrer à droite")).clicked() {
            self.save_side(1, shared);
        }
    }

    fn edit_ui(&mut self, ui: &mut egui::Ui) {
        let Some(sides) = &mut self.edit else {
            return;
        };
        let row_h = ui.text_style_height(&egui::TextStyle::Monospace);
        let rows = ((ui.available_height() / row_h) as usize)
            .saturating_sub(2)
            .max(4);
        let salt = self.id;
        ui.columns(2, |cols| {
            for (i, ui) in cols.iter_mut().enumerate() {
                let side = &mut sides[i];
                ui.weak(format!(
                    "{}{}",
                    if side.dirty { "*" } else { "" },
                    side.path.display()
                ));
                ScrollArea::both()
                    .id_salt(salt.with(("edit", i)))
                    .auto_shrink(false)
                    .show(ui, |ui| {
                        let r = TextEdit::multiline(&mut side.text)
                            .id(salt.with(("text", i)))
                            .code_editor()
                            .desired_width(f32::INFINITY)
                            .desired_rows(rows)
                            .show(ui)
                            .response;
                        side.dirty |= r.changed();
                    });
            }
        });
    }

    fn content(&mut self, ui: &mut egui::Ui) {
        if self.mode == Mode::Edit && self.edit.is_some() {
            return self.edit_ui(ui);
        }
        let salt = self.id.with("rows");
        let current = self.current;
        let scroll = self.scroll_to.take();
        match &self.outcome {
            Outcome::None => {
                ui.weak(if self.rx.is_some() {
                    t("Comparing…", "Comparaison…")
                } else {
                    ""
                });
            }
            Outcome::Error(e) => {
                ui.colored_label(ui.visuals().error_fg_color, format!("{}: {e}", tr("error")));
            }
            Outcome::Text { rows, .. } => {
                if rows
                    .iter()
                    .all(|r| r.kind == crate::core::compare::RowKind::Equal)
                {
                    ui.label(t(
                        "The files are identical.",
                        "Les fichiers sont identiques.",
                    ));
                }
                let block: Option<Range<usize>> = current.map(|c| diff_view::block_range(rows, c));
                diff_view::rows_ui(ui, salt, rows, block, scroll);
            }
            Outcome::Binary {
                identical,
                first,
                peek,
            } => {
                diff_view::binary_ui(ui, *identical, *first, peek.as_ref());
            }
        }
    }
}

impl ToolWindow for CompareWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        if !self.started {
            self.started = true;
            self.compare_files(ctx);
        }
        self.poll();
        let mut open = true;
        egui::Window::new(tr("cmd.compare_files"))
            .id(self.id)
            .open(&mut open)
            .default_size([1000.0, 650.0])
            .show(ctx, |ui| {
                self.toolbar(ui, shared);
                ui.separator();
                self.content(ui);
            });
        open
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{Duration, Instant};

    fn wait(w: &mut CompareWindow) {
        let end = Instant::now() + Duration::from_secs(5);
        while w.rx.is_some() && Instant::now() < end {
            w.poll();
            std::thread::sleep(Duration::from_millis(5));
        }
    }

    #[test]
    fn compares_in_the_background_and_reports_blocks() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a.txt"), dir.path().join("b.txt"));
        std::fs::write(&a, "x\ny\nz\n").unwrap();
        std::fs::write(&b, "x\nY\nz\nw\n").unwrap();
        let mut w = CompareWindow::new(a, b);
        let ctx = egui::Context::default();
        w.compare_files(&ctx);
        wait(&mut w);
        let Outcome::Text { blocks, rows } = &w.outcome else {
            panic!("expected a text result");
        };
        assert_eq!((*blocks, rows.len()), (2, 4));
        w.goto_difference(true);
        assert_eq!(w.current, Some(1));
        w.goto_difference(true);
        assert_eq!(w.current, Some(3));
        w.goto_difference(false);
        assert_eq!(w.current, Some(1));
    }

    #[test]
    fn binary_files_report_the_first_offset() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a.bin"), dir.path().join("b.bin"));
        std::fs::write(&a, [0u8, 1, 2, 3]).unwrap();
        std::fs::write(&b, [0u8, 1, 9, 3]).unwrap();
        let mut w = CompareWindow::new(a, b);
        w.compare_files(&egui::Context::default());
        wait(&mut w);
        match &w.outcome {
            Outcome::Binary {
                identical: false,
                first: Some(2),
                peek: Some(p),
            } => {
                assert_eq!(p.left[2], 2);
                assert_eq!(p.right[2], 9);
            }
            _ => panic!("expected a binary difference at offset 2"),
        }
    }

    #[test]
    fn missing_file_is_an_inline_error() {
        let dir = tempfile::tempdir().unwrap();
        let mut w = CompareWindow::new(dir.path().join("nope"), dir.path().join("nope2"));
        w.compare_files(&egui::Context::default());
        wait(&mut w);
        assert!(matches!(w.outcome, Outcome::Error(_)));
    }

    #[test]
    fn edit_buffers_recompare_and_save_keeps_line_endings() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a.txt"), dir.path().join("b.txt"));
        std::fs::write(&a, "one\r\ntwo\r\n").unwrap();
        std::fs::write(&b, "one\ntwo\n").unwrap();
        let mut w = CompareWindow::new(a.clone(), b);
        w.enter_edit();
        assert!(w.mode == Mode::Edit);
        let sides = w.edit.as_mut().unwrap();
        sides[0].text = "one\nTWO\n".into();
        sides[0].dirty = true;
        w.recompare_buffers(&egui::Context::default());
        wait(&mut w);
        assert!(matches!(w.outcome, Outcome::Text { blocks: 1, .. }));
        let (tx, rx) = std::sync::mpsc::channel();
        let shared = Shared {
            settings: crate::config::Settings::default(),
            passwords: None,
            tx,
            active_dir: None,
            other_dir: None,
            active_selection: Vec::new(),
        };
        w.save_side(0, &shared);
        assert_eq!(std::fs::read(&a).unwrap(), b"one\r\nTWO\r\n");
        assert!(matches!(rx.try_recv(), Ok(AppEvent::Reload)));
    }
}

#[cfg(test)]
mod render_tests {
    use super::*;

    #[test]
    fn window_renders_text_binary_and_edit_modes() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a.txt"), dir.path().join("b.txt"));
        std::fs::write(&a, "x\ny\nz\n").unwrap();
        std::fs::write(&b, "x\nY\nz\nw\n").unwrap();
        let mut w = CompareWindow::new(a, b);
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
        let mut pass = |w: &mut CompareWindow| {
            ctx.run_pass(egui::RawInput::default(), |c| {
                assert!(w.show(c, &mut shared));
            })
            .drop_without_applying_deltas();
        };
        pass(&mut w);
        let end = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while w.rx.is_some() && std::time::Instant::now() < end {
            pass(&mut w);
            std::thread::sleep(std::time::Duration::from_millis(5));
        }
        w.goto_difference(true);
        pass(&mut w);
        w.enter_edit();
        pass(&mut w);
        w.mode = Mode::View;
        pass(&mut w);
    }
}
