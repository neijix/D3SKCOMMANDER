//! Lister (F3): text, hex, image, DBF and catalog viewer.

mod table;
mod text;

use std::fs::File;
use std::io::Read;
use std::ops::Range;
use std::path::{Path, PathBuf};

use eframe::egui::{self, Color32, Key, Label, RichText, ScrollArea, TextStyle};

use super::{Shared, ToolWindow};
use crate::core::fs::human_size;
use crate::core::launcher::open_with_default;
use crate::i18n::{t, tr};
use table::Table;
use text::{Encoding, MAX_READ, VisualRow};

pub(super) use text::format_hex_row;

const IMAGE_EXTENSIONS: [&str; 9] = [
    "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "tif", "tiff",
];

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum Mode {
    Text,
    Hex,
    Image,
    Table,
}

/// A decoded text with its line index.
struct TextView {
    text: String,
    lines: Vec<Range<usize>>,
}

/// The viewer content, also embedded by the quick view panel.
pub struct Lister {
    path: PathBuf,
    id: egui::Id,
    size: u64,
    truncated: bool,
    mode: Mode,
    modes: Vec<Mode>,
    bytes: Vec<u8>,
    bytes_loaded: bool,
    error: Option<String>,
    encoding: Encoding,
    text: Option<TextView>,
    /// Wrapped rows, cached with the column count they were built for.
    wrapped: Option<(usize, Vec<VisualRow>)>,
    table: Option<Table>,
    filter: String,
    filtered: Vec<usize>,
    wrap: bool,
    image_fit: bool,
    find: String,
    case: bool,
    text_match: Option<usize>,
    hex_match: Option<usize>,
    /// Display row to scroll to on the next frame.
    scroll_to: Option<usize>,
    status: String,
}

impl Lister {
    /// Reads at most 16 MiB; failures end up in the content, never as a panic.
    pub fn open(path: &Path) -> Lister {
        let ext = path
            .extension()
            .map(|e| e.to_string_lossy().to_lowercase())
            .unwrap_or_default();
        let mut l = Lister {
            path: path.to_path_buf(),
            id: super::window_id("lister-view"),
            size: std::fs::metadata(path).map(|m| m.len()).unwrap_or(0),
            truncated: false,
            mode: Mode::Text,
            modes: vec![Mode::Text, Mode::Hex],
            bytes: Vec::new(),
            bytes_loaded: false,
            error: None,
            encoding: Encoding::Utf8,
            text: None,
            wrapped: None,
            table: None,
            filter: String::new(),
            filtered: Vec::new(),
            wrap: true,
            image_fit: true,
            find: String::new(),
            case: false,
            text_match: None,
            hex_match: None,
            scroll_to: None,
            status: String::new(),
        };
        if IMAGE_EXTENSIONS.contains(&ext.as_str()) {
            l.mode = Mode::Image;
            l.modes = vec![Mode::Image, Mode::Hex];
        } else if ext == "dbf" {
            match table::load_dbf(path) {
                Ok(tb) => l.set_table(tb, vec![Mode::Table, Mode::Hex]),
                Err(e) => l.error = Some(format!("{}: {e}", tr("error"))),
            }
        } else if let Some(tb) = table::load_catalog(path) {
            l.set_table(tb, vec![Mode::Table, Mode::Text]);
        } else {
            l.load_bytes();
            if text::is_binary(&l.bytes) {
                l.mode = Mode::Hex;
            }
        }
        l
    }

    fn set_table(&mut self, tb: Table, modes: Vec<Mode>) {
        self.filtered = tb.filtered("");
        self.table = Some(tb);
        self.mode = Mode::Table;
        self.modes = modes;
    }

    fn load_bytes(&mut self) {
        if self.bytes_loaded {
            return;
        }
        self.bytes_loaded = true;
        let mut buf = Vec::new();
        let read =
            File::open(&self.path).and_then(|f| f.take(MAX_READ as u64 + 1).read_to_end(&mut buf));
        match read {
            Ok(_) => {
                self.truncated = buf.len() > MAX_READ;
                buf.truncate(MAX_READ);
                self.encoding = text::detect_encoding(&buf).0;
                self.bytes = buf;
            }
            Err(e) => self.error = Some(format!("{}: {e}", tr("error"))),
        }
    }

    fn ensure_text(&mut self) {
        if self.text.is_none() {
            let (text, enc) = text::decode(&self.bytes);
            self.encoding = enc;
            let lines = text::line_ranges(&text);
            self.text = Some(TextView { text, lines });
        }
    }

    /// Loads what the current mode needs.
    fn prepare(&mut self) {
        if matches!(self.mode, Mode::Text | Mode::Hex) {
            self.load_bytes();
            if self.error.is_none() && self.mode == Mode::Text {
                self.ensure_text();
            }
        }
    }

    pub fn ui(&mut self, ui: &mut egui::Ui) {
        self.toolbar(ui);
        ui.separator();
        self.prepare();
        if let Some(e) = &self.error {
            ui.colored_label(ui.visuals().error_fg_color, e);
            return;
        }
        match self.mode {
            Mode::Text => self.text_ui(ui),
            Mode::Hex => self.hex_ui(ui),
            Mode::Image => self.image_ui(ui),
            Mode::Table => self.table_ui(ui),
        }
    }

    fn toolbar(&mut self, ui: &mut egui::Ui) {
        ui.horizontal_wrapped(|ui| {
            for m in self.modes.clone() {
                let label = match m {
                    Mode::Text => t("Text", "Texte"),
                    Mode::Hex => "Hex",
                    Mode::Image => t("Image", "Image"),
                    Mode::Table => t("Table", "Tableau"),
                };
                ui.selectable_value(&mut self.mode, m, label);
            }
            ui.separator();
            match self.mode {
                Mode::Text => {
                    ui.checkbox(&mut self.wrap, t("Wrap", "Retour à la ligne"));
                }
                Mode::Image => {
                    ui.checkbox(
                        &mut self.image_fit,
                        t("Fit to window", "Ajuster à la fenêtre"),
                    );
                }
                _ => {}
            }
            if ui
                .button(t(
                    "Open with default app",
                    "Ouvrir avec l'application par défaut",
                ))
                .clicked()
                && let Err(e) = open_with_default(&self.path)
            {
                self.status = e;
            }
            ui.weak(self.info());
        });
        match self.mode {
            Mode::Text | Mode::Hex if self.error.is_none() => self.find_bar(ui),
            Mode::Table => self.filter_bar(ui),
            _ => {}
        }
        if !self.status.is_empty() {
            ui.colored_label(ui.visuals().warn_fg_color, &self.status);
        }
    }

    fn info(&self) -> String {
        let mut s = human_size(self.size);
        if self.mode == Mode::Text {
            s.push_str(&format!(" · {}", self.encoding));
        }
        if let (Some(tb), Mode::Table) = (&self.table, self.mode) {
            s.push_str(&format!(" · {} · {}", tb.note, tb.rows.len()));
        }
        if self.truncated && matches!(self.mode, Mode::Text | Mode::Hex) {
            s.push_str(&format!(
                " · {} {}",
                t("truncated: first", "tronqué : premiers"),
                human_size(MAX_READ as u64)
            ));
        }
        s
    }

    fn find_bar(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.label(t("Find", "Chercher"));
            let r = ui.add(egui::TextEdit::singleline(&mut self.find).desired_width(220.0));
            let enter = r.lost_focus() && ui.input(|i| i.key_pressed(Key::Enter));
            let shift = ui.input(|i| i.modifiers.shift);
            if r.changed() {
                self.status.clear();
            }
            let next = ui.button(t("Next", "Suivant")).clicked() || (enter && !shift);
            let prev = ui.button(t("Previous", "Précédent")).clicked() || (enter && shift);
            ui.checkbox(&mut self.case, t("Match case", "Respecter la casse"));
            if next || prev {
                self.do_find(next);
            }
        });
    }

    fn filter_bar(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.label(t("Filter", "Filtre"));
            if ui
                .add(egui::TextEdit::singleline(&mut self.filter).desired_width(260.0))
                .changed()
                && let Some(tb) = &self.table
            {
                self.filtered = tb.filtered(&self.filter);
            }
            ui.weak(format!(
                "{} / {}",
                self.filtered.len(),
                self.table.as_ref().map_or(0, |tb| tb.rows.len())
            ));
        });
    }

    /// Jumps to the next (`forward`) or previous match of the find box.
    fn do_find(&mut self, forward: bool) {
        self.status.clear();
        if self.find.is_empty() {
            return;
        }
        self.prepare();
        let found = match self.mode {
            Mode::Text => self.find_text(forward),
            Mode::Hex => self.find_hex(forward),
            _ => return,
        };
        if !found {
            self.status = t("Not found", "Introuvable").into();
        }
    }

    fn find_text(&mut self, forward: bool) -> bool {
        let Some(tv) = &self.text else {
            return false;
        };
        let n = tv.lines.len();
        if n == 0 {
            return false;
        }
        let from = match (self.text_match, forward) {
            (Some(l), true) => l + 1,
            (Some(l), false) => l + n - 1,
            (None, true) => 0,
            (None, false) => n - 1,
        };
        let hit = text::find_line(&tv.text, &tv.lines, &self.find, self.case, from, forward);
        if let Some(line) = hit {
            self.text_match = Some(line);
            self.scroll_to = Some(match &self.wrapped {
                Some((_, rows)) if self.wrap => rows.partition_point(|r| r.line < line),
                _ => line,
            });
        }
        hit.is_some()
    }

    fn find_hex(&mut self, forward: bool) -> bool {
        let needle = self.find.as_bytes();
        let from = match (self.hex_match, forward) {
            (Some(o), true) => o + 1,
            (Some(o), false) => o.checked_sub(1).unwrap_or(usize::MAX),
            (None, true) => 0,
            (None, false) => usize::MAX,
        };
        let hit = text::find_bytes(&self.bytes, needle, self.case, from, forward);
        if let Some(off) = hit {
            self.hex_match = Some(off);
            self.scroll_to = Some(off / text::HEX_WIDTH);
        }
        hit.is_some()
    }

    fn text_ui(&mut self, ui: &mut egui::Ui) {
        let Some(tv) = &self.text else {
            return;
        };
        let font = TextStyle::Monospace.resolve(ui.style());
        let row_h = ui.text_style_height(&TextStyle::Monospace);
        let step = row_h + ui.spacing().item_spacing.y;
        let char_w = ui.ctx().fonts_mut(|f| f.glyph_width(&font, '0')).max(1.0);
        let digits = tv.lines.len().to_string().len().max(3);
        let gutter = (digits as f32 + 1.0) * char_w;
        let wrap = self.wrap;
        if wrap {
            let cols = (((ui.available_width() - gutter - 28.0) / char_w) as usize).max(8);
            if self.wrapped.as_ref().is_none_or(|(c, _)| *c != cols) {
                let rows = text::wrap_rows(&tv.text, &tv.lines, cols);
                self.wrapped = Some((cols, rows));
            }
        }
        let rows = self.wrapped.as_ref().filter(|_| wrap).map(|(_, r)| r);
        let n = rows.map_or(tv.lines.len(), Vec::len);
        let mut area = if wrap {
            ScrollArea::vertical()
        } else {
            ScrollArea::both()
        }
        .id_salt(self.id.with("text"))
        .auto_shrink(false);
        if let Some(r) = self.scroll_to.take() {
            area = area.vertical_scroll_offset(r.saturating_sub(3) as f32 * step);
        }
        let hl = highlight_color();
        let current = self.text_match;
        area.show_rows(ui, row_h, n, |ui, range| {
            row_style(ui, row_h, char_w);
            for i in range {
                let (no, slice) = match rows {
                    Some(rows) => {
                        let r = &rows[i];
                        (r.first.then_some(r.line), &tv.text[r.range.clone()])
                    }
                    None => (Some(i), &tv.text[tv.lines[i].clone()]),
                };
                let line_idx = rows.map_or(i, |r| r[i].line);
                let shown = text::clip_line(slice);
                ui.horizontal(|ui| {
                    let num = no.map_or_else(String::new, |n| (n + 1).to_string());
                    ui.add(
                        Label::new(RichText::new(format!("{num:>digits$}")).monospace().weak())
                            .selectable(false),
                    );
                    let mut rt = RichText::new(shown.as_ref()).monospace();
                    if current == Some(line_idx) {
                        rt = rt.background_color(hl);
                    }
                    ui.add(Label::new(rt).selectable(true).extend());
                });
            }
        });
    }

    fn hex_ui(&mut self, ui: &mut egui::Ui) {
        let row_h = ui.text_style_height(&TextStyle::Monospace);
        let step = row_h + ui.spacing().item_spacing.y;
        let char_w = ui
            .ctx()
            .fonts_mut(|f| f.glyph_width(&TextStyle::Monospace.resolve(ui.style()), '0'));
        let n = self.bytes.len().div_ceil(text::HEX_WIDTH);
        let mut area = ScrollArea::both()
            .id_salt(self.id.with("hex"))
            .auto_shrink(false);
        if let Some(r) = self.scroll_to.take() {
            area = area.vertical_scroll_offset(r.saturating_sub(3) as f32 * step);
        }
        let hl = highlight_color();
        let current = self.hex_match.map(|o| o / text::HEX_WIDTH);
        let bytes = &self.bytes;
        area.show_rows(ui, row_h, n, |ui, range| {
            row_style(ui, row_h, char_w);
            for i in range {
                let start = i * text::HEX_WIDTH;
                let end = (start + text::HEX_WIDTH).min(bytes.len());
                let mut rt =
                    RichText::new(text::format_hex_row(start, &bytes[start..end])).monospace();
                if current == Some(i) {
                    rt = rt.background_color(hl);
                }
                ui.add(Label::new(rt).selectable(true).extend());
            }
        });
    }

    fn image_ui(&mut self, ui: &mut egui::Ui) {
        let uri = format!("file://{}", self.path.display());
        if self.image_fit {
            let avail = ui.available_size();
            ui.centered_and_justified(|ui| {
                ui.add(egui::Image::from_uri(uri).shrink_to_fit().max_size(avail));
            });
        } else {
            ScrollArea::both()
                .id_salt(self.id.with("image"))
                .auto_shrink(false)
                .show(ui, |ui| {
                    ui.add(egui::Image::from_uri(uri).fit_to_original_size(1.0));
                });
        }
    }

    fn table_ui(&mut self, ui: &mut egui::Ui) {
        use egui_extras::{Column, TableBuilder};
        let Some(tb) = &self.table else {
            return;
        };
        let row_h = ui.text_style_height(&TextStyle::Body) + 4.0;
        let char_w = ui
            .ctx()
            .fonts_mut(|f| f.glyph_width(&TextStyle::Body.resolve(ui.style()), '0'));
        let filtered = &self.filtered;
        ScrollArea::horizontal()
            .id_salt(self.id.with("table-h"))
            .auto_shrink(false)
            .show(ui, |ui| {
                let mut builder = TableBuilder::new(ui)
                    .id_salt(self.id.with("table"))
                    .striped(true)
                    .resizable(true)
                    .max_scroll_height(f32::INFINITY);
                for w in &tb.widths {
                    let px = (*w as f32 * char_w + 16.0).clamp(40.0, 520.0);
                    builder = builder.column(Column::initial(px).clip(true));
                }
                builder
                    .header(row_h, |mut h| {
                        for c in &tb.columns {
                            h.col(|ui| {
                                ui.strong(c);
                            });
                        }
                    })
                    .body(|body| {
                        body.rows(row_h, filtered.len(), |mut row| {
                            let cells = &tb.rows[filtered[row.index()]];
                            for c in cells {
                                row.col(|ui| {
                                    ui.add(Label::new(c).selectable(true).truncate());
                                });
                            }
                        });
                    });
            });
    }
}

/// Keeps every row exactly `row_h` high (what `show_rows` assumes) and sets the gutter gap.
fn row_style(ui: &mut egui::Ui, row_h: f32, char_w: f32) {
    let sp = ui.spacing_mut();
    sp.item_spacing.x = char_w;
    sp.interact_size.y = row_h;
}

/// A translucent highlight that reads on dark and light themes.
fn highlight_color() -> Color32 {
    Color32::from_rgba_unmultiplied(255, 190, 0, 90)
}

pub struct ListerWindow {
    id: egui::Id,
    title: String,
    lister: Lister,
}

impl ListerWindow {
    pub fn new(path: PathBuf) -> Self {
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| path.display().to_string());
        Self {
            id: super::window_id("lister"),
            title: format!("Lister — {name}"),
            lister: Lister::open(&path),
        }
    }
}

impl ToolWindow for ListerWindow {
    fn show(&mut self, ctx: &egui::Context, _shared: &mut Shared) -> bool {
        let mut open = true;
        egui::Window::new(self.title.as_str())
            .id(self.id)
            .open(&mut open)
            .default_size([900.0, 650.0])
            .show(ctx, |ui| self.lister.ui(ui));
        open
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn render(l: &mut Lister, passes: usize) {
        let ctx = egui::Context::default();
        for _ in 0..passes {
            ctx.run_ui(egui::RawInput::default(), |ui| l.ui(ui))
                .drop_without_applying_deltas();
        }
    }

    #[test]
    fn text_file_renders_wrapped_and_unwrapped_and_finds() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("a.txt");
        let body: String = (0..2000)
            .map(|i| format!("line {i} {}\n", "x".repeat(i % 300)))
            .collect();
        std::fs::write(&p, body).unwrap();
        let mut l = Lister::open(&p);
        assert_eq!(l.mode, Mode::Text);
        render(&mut l, 2);
        l.find = "LINE 1500 ".into();
        l.do_find(true);
        assert_eq!(l.text_match, Some(1500));
        l.do_find(true);
        assert_eq!(l.text_match, Some(1500), "a single match stays put");
        l.case = true;
        l.do_find(false);
        assert!(!l.status.is_empty(), "case-sensitive search must miss");
        l.wrap = false;
        render(&mut l, 2);
        l.mode = Mode::Hex;
        render(&mut l, 2);
        l.find = "line 7 ".into();
        l.do_find(true);
        assert!(l.hex_match.is_some());
    }

    #[test]
    fn binary_file_opens_in_hex_and_missing_file_shows_an_error() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("b.bin");
        std::fs::write(&p, [0u8, 1, 2, 255, 16, 17]).unwrap();
        let mut l = Lister::open(&p);
        assert_eq!(l.mode, Mode::Hex);
        render(&mut l, 2);
        let mut missing = Lister::open(&dir.path().join("none"));
        render(&mut missing, 2);
        assert!(missing.error.is_some());
    }

    #[test]
    fn large_file_is_truncated() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("big.txt");
        let f = std::fs::File::create(&p).unwrap();
        f.set_len(MAX_READ as u64 + 10).unwrap();
        let l = Lister::open(&p);
        assert!(l.truncated && l.bytes.len() == MAX_READ);
    }

    #[test]
    fn catalog_and_window_render() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("d.cat");
        std::fs::write(
            &p,
            "C:\\r\nsub\\\t0\t2024.01.02\t10:11.12\na.txt\t5\t2024.01.02\t10:11.13\n",
        )
        .unwrap();
        let mut l = Lister::open(&p);
        assert_eq!(l.mode, Mode::Table);
        render(&mut l, 2);
        l.filter = "a.txt".into();
        l.filtered = l.table.as_ref().unwrap().filtered(&l.filter);
        assert_eq!(l.filtered, vec![1]);
        let mut w = ListerWindow::new(p);
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
        for _ in 0..2 {
            ctx.run_pass(egui::RawInput::default(), |c| {
                assert!(w.show(c, &mut shared));
            })
            .drop_without_applying_deltas();
        }
    }
}
