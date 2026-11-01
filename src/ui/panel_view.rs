//! Drawing the two panels: path bar, tabs, quick filter, file list (full, brief or
//! thumbnails), context menu and status line.

use std::path::{Path, PathBuf};

use eframe::egui::{self, Align, Color32, RichText, Sense};
use egui_extras::{Column, TableBuilder};

use super::App;
use super::dialogs::Dialog;
use super::keys::Command;
use super::panel::{Location, ViewMode, is_browsable_archive};
use crate::core::fs::{self as cfs, Entry, SortKey};
use crate::i18n::{t, tr};

/// What the user did in a panel this frame; applied once drawing is over.
enum Act {
    Activate,
    Click {
        index: usize,
        ctrl: bool,
        shift: bool,
    },
    Open(usize),
    Menu(Command),
    Sort(SortKey),
    Tab(usize),
    CloseTab(usize),
    Go(Location),
    Drives,
}

const ROW_HEIGHT: f32 = 20.0;
const IMAGE_EXTS: &[&str] = &[
    "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "tif", "tiff",
];

impl App {
    pub(crate) fn panels_ui(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        let mut acts: Vec<(usize, Act)> = Vec::new();
        ui.columns(2, |cols| {
            for (side, col) in cols.iter_mut().enumerate() {
                if self.quick_view.is_some() && side != self.active {
                    self.quick_view_ui(col);
                } else {
                    for a in self.panel_ui(col, side) {
                        acts.push((side, a));
                    }
                }
            }
        });
        for (side, act) in acts {
            self.apply_act(side, act, &ctx);
        }
    }

    fn apply_act(&mut self, side: usize, act: Act, ctx: &egui::Context) {
        let hidden = self.show_hidden();
        self.active = side;
        let tab = self.panels[side].tab_mut();
        match act {
            Act::Activate => {}
            Act::Click { index, ctrl, shift } => {
                if ctrl {
                    if let Some(e) = tab.visible.get(index).map(|&i| tab.entries[i].clone())
                        && !e.is_parent
                    {
                        tab.toggle_selection(&e.path);
                    }
                } else if shift {
                    let (a, b) = (tab.cursor.min(index), tab.cursor.max(index));
                    for v in a..=b {
                        let e = &tab.entries[tab.visible[v]];
                        if !e.is_parent {
                            let p = e.path.clone();
                            tab.selected.insert(p);
                        }
                    }
                }
                tab.cursor = index;
                // A click in the list takes the keyboard back from any text field.
                ctx.memory_mut(|m| {
                    if let Some(f) = m.focused() {
                        m.surrender_focus(f);
                    }
                });
            }
            Act::Open(index) => {
                tab.cursor = index;
                self.open_current(ctx);
            }
            Act::Menu(cmd) => self.run(cmd, ctx),
            Act::Sort(key) => tab.set_sort(key),
            Act::Tab(i) => self.panels[side].active_tab = i,
            Act::CloseTab(i) => {
                let p = &mut self.panels[side];
                p.active_tab = i;
                p.close_tab();
            }
            Act::Go(loc) => {
                tab.go(loc, hidden);
            }
            Act::Drives => self.dialog = Some(Dialog::Drives { side }),
        }
    }

    fn panel_ui(&mut self, ui: &mut egui::Ui, side: usize) -> Vec<Act> {
        let mut acts = Vec::new();
        let active = side == self.active;
        let stroke_color = if active {
            ui.visuals().selection.stroke.color
        } else {
            ui.visuals().widgets.noninteractive.bg_stroke.color
        };
        let status = self.status_text(side);
        let focus_filter = active && std::mem::take(&mut self.focus_filter);
        let columns = self.shared.settings.columns.clone();
        egui::Frame::new()
            .stroke(egui::Stroke::new(
                if active { 1.5 } else { 1.0 },
                stroke_color,
            ))
            .inner_margin(4.0)
            .show(ui, |ui| {
                ui.set_min_height(ui.available_height());
                ui.set_min_width(ui.available_width());
                let panel = &mut self.panels[side];
                tabs_bar(ui, panel, &mut acts);
                let tab = panel.tab_mut();
                path_bar(ui, tab, side, &mut acts);
                if let Some(filter) = &mut tab.filter {
                    let mut changed = false;
                    ui.horizontal(|ui| {
                        ui.label(format!("🔍 {}", tr("filter")));
                        let r =
                            ui.add(egui::TextEdit::singleline(filter).desired_width(f32::INFINITY));
                        if focus_filter {
                            r.request_focus();
                        }
                        changed = r.changed();
                        if r.has_focus() && ui.input(|i| i.key_pressed(egui::Key::Escape)) {
                            acts.push(Act::Activate);
                        }
                    });
                    if changed {
                        tab.apply_filter();
                    }
                }
                if let Some(err) = &tab.error {
                    ui.colored_label(ui.visuals().error_fg_color, err);
                }
                let list_height = (ui.available_height() - 24.0).max(60.0);
                let list = ui.allocate_ui(egui::vec2(ui.available_width(), list_height), |ui| {
                    ui.set_min_height(list_height);
                    match tab.view {
                        ViewMode::Full => full_view(ui, tab, active, &columns, side, &mut acts),
                        ViewMode::Brief => grid_view(ui, tab, active, false, &mut acts),
                        ViewMode::Thumbnails => grid_view(ui, tab, active, true, &mut acts),
                    }
                });
                if list.response.interact(Sense::click()).clicked() {
                    acts.push(Act::Activate);
                }
                ui.separator();
                ui.horizontal(|ui| {
                    ui.small(status);
                });
            });
        acts
    }

    fn status_text(&mut self, side: usize) -> String {
        let tab = self.panels[side].tab();
        let (sel_n, sel_size, all_n, all_size) = tab.selection_summary();
        let mut s = format!(
            "{} / {} {} — {} / {}",
            sel_n,
            all_n,
            tr("selected").to_lowercase(),
            cfs::human_size(sel_size),
            cfs::human_size(all_size)
        );
        if tab.filter.is_some() {
            s.push_str(&format!(" — {}", tr("st.filtered")));
        }
        let dir = match &tab.location {
            Location::Local(p) => Some(p.clone()),
            Location::Archive { .. } => {
                s.push_str(&format!(" — {}", tr("st.archive")));
                None
            }
            Location::List { .. } => None,
        };
        if let Some(dir) = dir
            && let Some(d) = best_drive(self.drives(), &dir)
        {
            s.push_str(&format!(
                " — {} {} {} {}",
                cfs::human_size(d.0),
                tr("free"),
                tr("of"),
                cfs::human_size(d.1)
            ));
        }
        if !self.status.is_empty() && side == self.active {
            s.push_str(&format!("   │ {}", self.status));
        }
        s
    }
}

/// (free, total) of the drive holding `dir` (longest mount-point prefix).
fn best_drive(drives: &[cfs::Drive], dir: &Path) -> Option<(u64, u64)> {
    drives
        .iter()
        .filter(|d| d.total > 0 && dir.starts_with(&d.path))
        .max_by_key(|d| d.path.as_os_str().len())
        .map(|d| (d.free, d.total))
}

fn tabs_bar(ui: &mut egui::Ui, panel: &mut super::panel::PanelState, acts: &mut Vec<Act>) {
    if panel.tabs.len() < 2 {
        return;
    }
    ui.horizontal_wrapped(|ui| {
        for (i, tab) in panel.tabs.iter().enumerate() {
            let r = ui
                .selectable_label(i == panel.active_tab, tab.location.title())
                .on_hover_text(tab.location.display());
            if r.clicked() {
                acts.push(Act::Tab(i));
            }
            if r.middle_clicked() {
                acts.push(Act::CloseTab(i));
            }
        }
    });
}

fn path_bar(ui: &mut egui::Ui, tab: &mut super::panel::Tab, side: usize, acts: &mut Vec<Act>) {
    ui.horizontal(|ui| {
        if ui
            .button("💾")
            .on_hover_text(if side == 0 { "Alt+F1" } else { "Alt+F2" })
            .clicked()
        {
            acts.push(Act::Drives);
        }
        if ui.button("⬆").on_hover_text(tr("cmd.parent")).clicked() {
            acts.push(Act::Menu(Command::Parent));
        }
        if ui.button("⭐").on_hover_text(tr("cmd.hotlist")).clicked() {
            acts.push(Act::Menu(Command::Hotlist));
        }
        if ui.button("🕘").on_hover_text(tr("cmd.history")).clicked() {
            acts.push(Act::Menu(Command::History));
        }
        match &mut tab.path_edit {
            Some(text) => {
                let r = ui.add(egui::TextEdit::singleline(text).desired_width(f32::INFINITY));
                r.request_focus();
                let enter = ui.input(|i| i.key_pressed(egui::Key::Enter));
                let escape = ui.input(|i| i.key_pressed(egui::Key::Escape));
                if enter {
                    if let Some(loc) = typed_location(text.trim()) {
                        acts.push(Act::Go(loc));
                    }
                    tab.path_edit = None;
                } else if escape || r.lost_focus() {
                    tab.path_edit = None;
                }
            }
            None => {
                let text = tab.location.display();
                let r = ui.add(
                    egui::Button::new(RichText::new(&text).monospace())
                        .frame(false)
                        .truncate(),
                );
                if r.on_hover_text(t("Click to type a path", "Cliquez pour saisir un chemin"))
                    .clicked()
                {
                    tab.path_edit = Some(text);
                }
            }
        }
    });
}

/// A typed path: a folder, or an archive to browse.
fn typed_location(text: &str) -> Option<Location> {
    let p = PathBuf::from(text);
    if p.is_dir() {
        Some(Location::Local(p))
    } else if p.is_file() && is_browsable_archive(&p) {
        Some(Location::Archive {
            file: p,
            inner: String::new(),
        })
    } else {
        None
    }
}

fn icon(e: &Entry) -> &'static str {
    if e.is_parent {
        "⬆"
    } else if e.is_dir {
        "📁"
    } else if e.is_link {
        "🔗"
    } else if is_browsable_archive(&e.path) {
        "📦"
    } else if IMAGE_EXTS.contains(&e.extension().to_lowercase().as_str()) {
        "🖼"
    } else {
        "📄"
    }
}

fn name_text(ui: &egui::Ui, e: &Entry, selected: bool, show_ext: bool) -> RichText {
    let name = if show_ext || e.is_dir {
        e.name.as_str()
    } else {
        e.stem()
    };
    let label = if e.is_parent {
        "..".to_string()
    } else {
        format!("{} {name}", icon(e))
    };
    let mut text = RichText::new(label);
    if selected {
        text = text.color(marked_color(ui)).strong();
    } else if e.hidden {
        text = text.weak();
    }
    text
}

fn marked_color(ui: &egui::Ui) -> Color32 {
    if ui.visuals().dark_mode {
        Color32::from_rgb(255, 120, 100)
    } else {
        Color32::from_rgb(200, 30, 20)
    }
}

/// Reads the click modifiers and turns a row's response into actions.
fn row_acts(r: &egui::Response, index: usize, acts: &mut Vec<Act>) {
    let mods = r.ctx.input(|i| i.modifiers);
    if r.double_clicked() {
        acts.push(Act::Open(index));
    } else if r.clicked() {
        acts.push(Act::Click {
            index,
            ctrl: mods.command,
            shift: mods.shift,
        });
    } else if r.secondary_clicked() {
        acts.push(Act::Click {
            index,
            ctrl: false,
            shift: false,
        });
    }
    r.context_menu(|ui| {
        use Command::*;
        for cmd in [
            View,
            Edit,
            Copy,
            Move,
            RenameInPlace,
            Delete,
            Pack,
            Unpack,
            OpenWith,
            CopyPaths,
            Properties,
        ] {
            if ui.button(cmd.label()).clicked() {
                acts.push(Act::Click {
                    index,
                    ctrl: false,
                    shift: false,
                });
                acts.push(Act::Menu(cmd));
                ui.close();
            }
        }
    });
}

fn full_view(
    ui: &mut egui::Ui,
    tab: &mut super::panel::Tab,
    active: bool,
    cols: &crate::config::Columns,
    side: usize,
    acts: &mut Vec<Act>,
) {
    let (mut table, headers) = full_table(ui, cols, side);
    if std::mem::take(&mut tab.scroll_to_cursor) {
        table = table.scroll_to_row(tab.cursor, None);
    }
    let tab = &*tab;
    table
        .header(ROW_HEIGHT, |mut h| {
            for (key, sk) in &headers {
                h.col(|ui| sort_header(ui, key, *sk, tab.sort, acts));
            }
        })
        .body(|body| {
            body.rows(ROW_HEIGHT, tab.visible.len(), |mut row| {
                full_row(&mut row, tab, active, cols, acts);
            });
        });
}

/// The table with one column per shown field, and the (title key, sort key) of each.
fn full_table<'a>(
    ui: &'a mut egui::Ui,
    cols: &crate::config::Columns,
    side: usize,
) -> (TableBuilder<'a>, Vec<(&'static str, SortKey)>) {
    let mut table = TableBuilder::new(ui)
        .id_salt(("panel", side))
        .striped(true)
        .resizable(true)
        .sense(Sense::click())
        .cell_layout(egui::Layout::left_to_right(Align::Center))
        .auto_shrink(false)
        .column(Column::remainder().at_least(120.0).clip(true));
    let mut headers = vec![("name", SortKey::Name)];
    for (on, key, sort, width) in [
        (cols.ext, "ext", SortKey::Ext, 50.0),
        (cols.size, "size", SortKey::Size, 80.0),
        (cols.date, "date", SortKey::Modified, 130.0),
        (cols.attrs, "attr", SortKey::Attrs, 70.0),
    ] {
        if on {
            table = table.column(Column::initial(width).at_least(30.0).clip(true));
            headers.push((key, sort));
        }
    }
    (table, headers)
}

/// A clickable column title with the sort direction arrow when it is the sort key.
fn sort_header(
    ui: &mut egui::Ui,
    key: &'static str,
    sk: SortKey,
    sort: cfs::SortOrder,
    acts: &mut Vec<Act>,
) {
    let arrow = match (sort.key == sk, sort.descending) {
        (true, false) => " ▲",
        (true, true) => " ▼",
        _ => "",
    };
    if ui
        .button(RichText::new(format!("{}{arrow}", tr(key))).strong())
        .clicked()
    {
        acts.push(Act::Sort(sk));
    }
}

fn full_row(
    row: &mut egui_extras::TableRow<'_, '_>,
    tab: &super::panel::Tab,
    active: bool,
    cols: &crate::config::Columns,
    acts: &mut Vec<Act>,
) {
    let index = row.index();
    let e = &tab.entries[tab.visible[index]];
    let marked = tab.selected.contains(&e.path);
    row.set_selected(active && index == tab.cursor);
    row.col(|ui| {
        ui.label(name_text(ui, e, marked, !cols.ext));
    });
    if cols.ext {
        row.col(|ui| {
            ui.label(e.extension());
        });
    }
    if cols.size {
        row.col(|ui| {
            let size = if e.is_parent {
                String::new()
            } else if e.is_dir {
                tab.dir_sizes
                    .get(&e.path)
                    .map(|s| cfs::grouped(*s))
                    .unwrap_or_else(|| format!("<{}>", tr("folder")))
            } else {
                cfs::grouped(e.size)
            };
            ui.with_layout(egui::Layout::right_to_left(Align::Center), |ui| {
                ui.label(size);
            });
        });
    }
    if cols.date {
        row.col(|ui| {
            if !e.is_parent {
                ui.label(cfs::format_time(e.modified));
            }
        });
    }
    if cols.attrs {
        row.col(|ui| {
            ui.monospace(&e.attrs);
        });
    }
    let r = row.response();
    row_acts(&r, index, acts);
}

fn grid_view(
    ui: &mut egui::Ui,
    tab: &mut super::panel::Tab,
    active: bool,
    thumbs: bool,
    acts: &mut Vec<Act>,
) {
    let scroll = std::mem::take(&mut tab.scroll_to_cursor);
    let local = !matches!(tab.location, Location::Archive { .. });
    egui::ScrollArea::vertical()
        .auto_shrink(false)
        .show(ui, |ui| {
            ui.horizontal_wrapped(|ui| {
                for (index, &i) in tab.visible.iter().enumerate() {
                    let e = &tab.entries[i];
                    let marked = tab.selected.contains(&e.path);
                    let is_cursor = active && index == tab.cursor;
                    let r = if thumbs {
                        let size = egui::vec2(128.0, 128.0);
                        ui.allocate_ui(size, |ui| {
                            ui.set_min_size(size);
                            ui.vertical_centered(|ui| {
                                let image = local
                                    && !e.is_dir
                                    && IMAGE_EXTS.contains(&e.extension().to_lowercase().as_str());
                                if image {
                                    ui.add(
                                        egui::Image::new(format!("file://{}", e.path.display()))
                                            .max_size(egui::vec2(120.0, 96.0))
                                            .maintain_aspect_ratio(true),
                                    );
                                } else {
                                    ui.label(RichText::new(icon(e)).size(56.0));
                                }
                                let label =
                                    egui::Label::new(name_text(ui, e, marked, true)).truncate();
                                ui.add(label);
                            });
                        })
                        .response
                        .interact(Sense::click())
                    } else {
                        ui.add_sized(
                            [200.0, ROW_HEIGHT],
                            egui::Button::selectable(is_cursor, name_text(ui, e, marked, true))
                                .truncate(),
                        )
                    };
                    if thumbs && is_cursor {
                        ui.painter().rect_stroke(
                            r.rect,
                            4.0,
                            ui.visuals().selection.stroke,
                            egui::StrokeKind::Inside,
                        );
                    }
                    if scroll && is_cursor {
                        r.scroll_to_me(Some(Align::Center));
                    }
                    row_acts(&r, index, acts);
                }
            });
        });
}
