//! Rendering of a compare result: the side-by-side rows and the binary hex peek.

use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::ops::Range;
use std::path::Path;

use eframe::egui::{self, Align2, Color32, FontId, Rect, ScrollArea, Sense, TextStyle, pos2, vec2};

use crate::core::compare::{DiffRow, RowKind};
use crate::i18n::t;
use crate::ui::lister::format_hex_row;

/// Rows of 16 bytes shown around the first differing byte.
const PEEK_ROWS: usize = 8;
const ROW_BYTES: usize = 16;

/// Bytes of both files around the first difference.
pub struct Peek {
    pub start: u64,
    pub first: u64,
    pub left: Vec<u8>,
    pub right: Vec<u8>,
}

/// Number of difference blocks (runs of non-equal rows).
pub fn block_count(rows: &[DiffRow]) -> usize {
    rows.iter()
        .enumerate()
        .filter(|(i, r)| {
            r.kind != RowKind::Equal && (*i == 0 || rows[i - 1].kind == RowKind::Equal)
        })
        .count()
}

/// The run of non-equal rows containing `row`.
pub fn block_range(rows: &[DiffRow], row: usize) -> Range<usize> {
    if rows.get(row).is_none_or(|r| r.kind == RowKind::Equal) {
        return row..row;
    }
    let start = (0..row)
        .rev()
        .take_while(|&i| rows[i].kind != RowKind::Equal)
        .last()
        .unwrap_or(row);
    let end = (row..rows.len())
        .take_while(|&i| rows[i].kind != RowKind::Equal)
        .last()
        .map_or(row, |i| i + 1);
    start..end
}

/// Offset of the first byte shown for a difference at `first`: two rows of context before it.
pub fn peek_start(first: u64) -> u64 {
    (first / ROW_BYTES as u64).saturating_sub(2) * ROW_BYTES as u64
}

/// Reads the bytes around `first` from both files.
pub fn read_peek(left: &Path, right: &Path, first: u64) -> std::io::Result<Peek> {
    let start = peek_start(first);
    let read = |p: &Path| -> std::io::Result<Vec<u8>> {
        let mut f = File::open(p)?;
        f.seek(SeekFrom::Start(start))?;
        let mut buf = Vec::new();
        f.take((PEEK_ROWS * ROW_BYTES) as u64)
            .read_to_end(&mut buf)?;
        Ok(buf)
    };
    Ok(Peek {
        start,
        first,
        left: read(left)?,
        right: read(right)?,
    })
}

fn kind_colors(kind: RowKind) -> (Color32, Color32) {
    let red = Color32::from_rgba_unmultiplied(255, 70, 70, 60);
    let green = Color32::from_rgba_unmultiplied(60, 200, 80, 60);
    let yellow = Color32::from_rgba_unmultiplied(255, 200, 0, 55);
    let gap = Color32::from_rgba_unmultiplied(128, 128, 128, 28);
    match kind {
        RowKind::Equal => (Color32::TRANSPARENT, Color32::TRANSPARENT),
        RowKind::Changed => (yellow, yellow),
        RowKind::LeftOnly => (red, gap),
        RowKind::RightOnly => (gap, green),
    }
}

/// Paints one side of a row: background, line number and clipped text.
fn paint_side(
    ui: &egui::Ui,
    rect: Rect,
    bg: Color32,
    no: Option<usize>,
    text: Option<&str>,
    num_w: f32,
) {
    let painter = ui.painter().with_clip_rect(rect);
    painter.rect_filled(rect, 0.0, bg);
    let font = FontId::monospace(ui.text_style_height(&TextStyle::Monospace) * 0.85);
    let y = rect.center().y;
    if let Some(n) = no {
        painter.text(
            pos2(rect.left() + num_w, y),
            Align2::RIGHT_CENTER,
            n,
            font.clone(),
            ui.visuals().weak_text_color(),
        );
    }
    if let Some(s) = text {
        let s = s.replace('\t', "    ");
        painter.text(
            pos2(rect.left() + num_w + 6.0, y),
            Align2::LEFT_CENTER,
            s,
            font,
            ui.visuals().text_color(),
        );
    }
}

/// The side-by-side rows, drawn only where visible. `scroll_to` is a row to bring into view.
pub fn rows_ui(
    ui: &mut egui::Ui,
    salt: egui::Id,
    rows: &[DiffRow],
    current: Option<Range<usize>>,
    scroll_to: Option<usize>,
) {
    let row_h = ui.text_style_height(&TextStyle::Monospace) + 2.0;
    let step = row_h + ui.spacing().item_spacing.y;
    let digits = rows
        .iter()
        .filter_map(|r| r.left_no.max(r.right_no))
        .max()
        .unwrap_or(1)
        .to_string()
        .len();
    let num_w = digits as f32 * 8.0 + 8.0;
    let mut area = ScrollArea::vertical().id_salt(salt).auto_shrink(false);
    if let Some(r) = scroll_to {
        area = area.vertical_scroll_offset(r.saturating_sub(3) as f32 * step);
    }
    let accent = ui.visuals().selection.bg_fill;
    area.show_rows(ui, row_h, rows.len(), |ui, range| {
        for i in range {
            let r = &rows[i];
            let (full, _) =
                ui.allocate_exact_size(vec2(ui.available_width(), row_h), Sense::hover());
            let half = (full.width() - 4.0) / 2.0;
            let left = Rect::from_min_size(full.min, vec2(half, row_h));
            let right = Rect::from_min_size(
                pos2(full.left() + half + 4.0, full.top()),
                vec2(half, row_h),
            );
            let (lbg, rbg) = kind_colors(r.kind);
            paint_side(ui, left, lbg, r.left_no, r.left.as_deref(), num_w);
            paint_side(ui, right, rbg, r.right_no, r.right.as_deref(), num_w);
            if current.as_ref().is_some_and(|c| c.contains(&i)) {
                let bar = Rect::from_min_size(full.min, vec2(3.0, row_h));
                ui.painter().rect_filled(bar, 0.0, accent);
            }
        }
    });
}

/// One file's hex rows: rows differing from `other` are tinted, the row of the first
/// difference more strongly.
fn peek_side(ui: &mut egui::Ui, peek: &Peek, mine: &[u8], other: &[u8]) {
    for row in 0..PEEK_ROWS {
        let lo = (row * ROW_BYTES).min(mine.len());
        let hi = (lo + ROW_BYTES).min(mine.len());
        let offset = peek.start as usize + row * ROW_BYTES;
        let differs = mine.get(lo..hi) != other.get(lo.min(other.len())..hi.min(other.len()));
        if lo == hi && !differs {
            break;
        }
        let mut rt = egui::RichText::new(format_hex_row(offset, &mine[lo..hi])).monospace();
        if (offset..offset + ROW_BYTES).contains(&(peek.first as usize)) {
            rt = rt.background_color(Color32::from_rgba_unmultiplied(255, 70, 70, 110));
        } else if differs {
            rt = rt.background_color(Color32::from_rgba_unmultiplied(255, 70, 70, 45));
        }
        ui.add(egui::Label::new(rt).selectable(true).extend());
    }
}

/// The binary result: identical, or the first difference with a hex peek of both files.
pub fn binary_ui(ui: &mut egui::Ui, identical: bool, first: Option<u64>, peek: Option<&Peek>) {
    if identical {
        ui.label(t(
            "The files are identical.",
            "Les fichiers sont identiques.",
        ));
        return;
    }
    if let Some(off) = first {
        ui.label(format!(
            "{} {off} (0x{off:X})",
            t(
                "Binary files differ; first difference at offset",
                "Fichiers binaires différents ; première différence à l'offset"
            )
        ));
    }
    let Some(p) = peek else {
        return;
    };
    ScrollArea::both()
        .id_salt("binary-peek")
        .auto_shrink(false)
        .show(ui, |ui| {
            ui.columns(2, |cols| {
                cols[0].strong(t("Left", "Gauche"));
                peek_side(&mut cols[0], p, &p.left, &p.right);
                cols[1].strong(t("Right", "Droite"));
                peek_side(&mut cols[1], p, &p.right, &p.left);
            });
        });
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::core::compare::{DiffOptions, diff_text};

    #[test]
    fn counts_and_locates_blocks() {
        let rows = diff_text(
            "a\nb\nc\nd\ne\n",
            "a\nX\nc\nd\nY\nZ\n",
            &DiffOptions::default(),
        );
        assert_eq!(block_count(&rows), 2);
        let first = block_range(&rows, 1);
        assert_eq!(first, 1..2);
        let last = block_range(&rows, rows.len() - 1);
        assert_eq!(last.end, rows.len());
        assert_eq!(block_range(&rows, 0), 0..0);
        assert_eq!(block_count(&[]), 0);
    }

    #[test]
    fn peek_starts_two_rows_before_the_difference() {
        assert_eq!(peek_start(0), 0);
        assert_eq!(peek_start(31), 0);
        assert_eq!(peek_start(16 * 5 + 3), 16 * 3);
    }

    #[test]
    fn reads_peek_from_both_files() {
        let dir = tempfile::tempdir().unwrap();
        let (a, b) = (dir.path().join("a"), dir.path().join("b"));
        let mut da = vec![1u8; 500];
        let db = da.clone();
        da[300] = 9;
        std::fs::write(&a, &da).unwrap();
        std::fs::write(&b, &db).unwrap();
        let p = read_peek(&a, &b, 300).unwrap();
        assert_eq!(p.start, 256);
        assert_eq!(p.left.len(), 128);
        assert_eq!(p.left[300 - 256], 9);
        assert_eq!(p.right[300 - 256], 1);
    }
}
