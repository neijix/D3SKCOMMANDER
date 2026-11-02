//! The remote listing table.

use std::time::SystemTime;

use eframe::egui;
use egui_extras::{Column, TableBuilder};

use super::FtpWindow;
use crate::core::fs::human_size;
use crate::i18n::tr;
use crate::ui::icons;

/// What happened in the table this frame. Row 0 is the ".." row; entry `i` is row `i + 1`.
#[derive(Default)]
pub struct Out {
    /// (row, add to the selection)
    pub click: Option<(usize, bool)>,
    pub double: Option<usize>,
}

impl FtpWindow {
    pub(super) fn table_ui(&self, ui: &mut egui::Ui) -> Out {
        let mut out = Out::default();
        if !self.connected() {
            return out;
        }
        let add = ui.input(|i| i.modifiers.command);
        TableBuilder::new(ui)
            .id_salt("ftp-remote")
            .striped(true)
            .resizable(true)
            .sense(egui::Sense::click())
            .column(Column::remainder().at_least(120.0).clip(true))
            .column(Column::initial(80.0).at_least(50.0))
            .column(Column::initial(120.0).at_least(80.0))
            .column(Column::initial(90.0).at_least(60.0))
            .header(20.0, |mut h| {
                for key in ["name", "size", "date"] {
                    h.col(|ui| {
                        ui.strong(tr(key));
                    });
                }
                h.col(|ui| {
                    ui.strong(tr("attr"));
                });
            })
            .body(|body| {
                body.rows(18.0, self.entries.len() + 1, |mut row| {
                    let idx = row.index();
                    let cells = match idx.checked_sub(1).and_then(|i| self.entries.get(i)) {
                        None => [
                            format!("{} ..", icons::UP),
                            String::new(),
                            String::new(),
                            String::new(),
                        ],
                        Some(e) => [
                            format!(
                                "{} {}",
                                if e.is_dir { icons::FOLDER } else { icons::FILE },
                                e.name
                            ),
                            if e.is_dir {
                                String::new()
                            } else {
                                human_size(e.size)
                            },
                            format_time(e.modified),
                            e.permissions.clone(),
                        ],
                    };
                    let selected = idx > 0
                        && self
                            .entries
                            .get(idx - 1)
                            .is_some_and(|e| self.selection.contains(&e.name));
                    row.set_selected(selected);
                    for text in cells {
                        row.col(|ui| {
                            ui.add(egui::Label::new(text).truncate().selectable(false));
                        });
                    }
                    let resp = row.response();
                    if resp.double_clicked() {
                        out.double = Some(idx);
                    } else if resp.clicked() {
                        out.click = Some((idx, add));
                    }
                });
            });
        out
    }
}

fn format_time(t: Option<SystemTime>) -> String {
    t.map(|t| {
        chrono::DateTime::<chrono::Local>::from(t)
            .format("%Y-%m-%d %H:%M")
            .to_string()
    })
    .unwrap_or_default()
}
