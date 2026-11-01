//! Synchronize directories: compare two folders on a worker thread, review and edit the
//! action of every difference, then apply them on a second thread with progress.

use std::io;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, TryRecvError, channel};
use std::time::Duration;

use eframe::egui::{self, Color32, RichText};
use egui_extras::{Column, TableBuilder};

use super::picker::{FolderPick, folder_field};
use super::{AppEvent, Shared, ToolWindow};
use crate::core::fs::{format_time, grouped};
use crate::core::sync::{
    Side, SyncAction, SyncItem, SyncOptions, SyncReport, SyncStatus, apply_sync, compare_dirs,
};
use crate::i18n::{t, tr};

/// The four status classes the filter toggles act on.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
struct Filter {
    equal: bool,
    newer: bool,
    left_only: bool,
    right_only: bool,
}

impl Default for Filter {
    fn default() -> Self {
        Filter {
            equal: false,
            newer: true,
            left_only: true,
            right_only: true,
        }
    }
}

impl Filter {
    fn shows(&self, status: SyncStatus) -> bool {
        match status {
            SyncStatus::Equal => self.equal,
            SyncStatus::LeftNewer | SyncStatus::RightNewer | SyncStatus::Different => self.newer,
            SyncStatus::LeftOnly => self.left_only,
            SyncStatus::RightOnly => self.right_only,
        }
    }
}

/// Indices of the items the filter lets through.
fn filter_indices(items: &[SyncItem], filter: &Filter) -> Vec<usize> {
    (0..items.len())
        .filter(|&i| filter.shows(items[i].status))
        .collect()
}

/// Item counts as `[equal, newer or different, left only, right only]`.
fn count_classes(items: &[SyncItem]) -> [usize; 4] {
    let mut n = [0; 4];
    for it in items {
        let slot = match it.status {
            SyncStatus::Equal => 0,
            SyncStatus::LeftNewer | SyncStatus::RightNewer | SyncStatus::Different => 1,
            SyncStatus::LeftOnly => 2,
            SyncStatus::RightOnly => 3,
        };
        n[slot] += 1;
    }
    n
}

fn status_symbol(status: SyncStatus) -> &'static str {
    match status {
        SyncStatus::Equal => "=",
        SyncStatus::Different => "≠",
        SyncStatus::LeftNewer | SyncStatus::LeftOnly => "→",
        SyncStatus::RightNewer | SyncStatus::RightOnly => "←",
    }
}

fn status_color(status: SyncStatus) -> Color32 {
    match status {
        SyncStatus::Equal => Color32::GRAY,
        SyncStatus::Different => Color32::from_rgb(220, 150, 40),
        SyncStatus::LeftNewer | SyncStatus::LeftOnly => Color32::from_rgb(70, 150, 230),
        SyncStatus::RightNewer | SyncStatus::RightOnly => Color32::from_rgb(80, 190, 110),
    }
}

/// The actions the combo may offer for an item of this status.
fn valid_actions(status: SyncStatus) -> &'static [SyncAction] {
    use SyncAction::*;
    match status {
        SyncStatus::Equal => &[None],
        SyncStatus::LeftOnly => &[None, CopyToRight, DeleteLeft],
        SyncStatus::RightOnly => &[None, CopyToLeft, DeleteRight],
        SyncStatus::LeftNewer | SyncStatus::RightNewer | SyncStatus::Different => {
            &[None, CopyToRight, CopyToLeft]
        }
    }
}

fn action_label(action: SyncAction) -> &'static str {
    match action {
        SyncAction::None => t("Skip", "Ignorer"),
        SyncAction::CopyToRight => t("Copy to right", "Copier à droite"),
        SyncAction::CopyToLeft => t("Copy to left", "Copier à gauche"),
        SyncAction::DeleteRight => t("Delete right", "Supprimer à droite"),
        SyncAction::DeleteLeft => t("Delete left", "Supprimer à gauche"),
    }
}

/// Arrow pointing at the side that changes.
fn action_arrow(action: SyncAction) -> &'static str {
    match action {
        SyncAction::None => "",
        SyncAction::CopyToRight => "→",
        SyncAction::CopyToLeft => "←",
        SyncAction::DeleteRight => "→ ✕",
        SyncAction::DeleteLeft => "✕ ←",
    }
}

/// Paths of every non-equal item: the left file, or the right one when only it exists.
fn feed_paths(items: &[SyncItem], left: &Path, right: &Path) -> Vec<PathBuf> {
    items
        .iter()
        .filter(|it| it.status != SyncStatus::Equal)
        .map(|it| {
            if it.left.is_some() {
                left.join(&it.rel)
            } else {
                right.join(&it.rel)
            }
        })
        .collect()
}

fn pending_count(items: &[SyncItem]) -> usize {
    items
        .iter()
        .filter(|i| i.action != SyncAction::None)
        .count()
}

/// Size and date cells of one side (blank when the item is missing there).
fn side_text(side: Option<Side>, is_dir: bool) -> (String, String) {
    match side {
        None => (String::new(), String::new()),
        Some(s) => (
            if is_dir {
                "<DIR>".into()
            } else {
                grouped(s.size)
            },
            format_time(s.modified),
        ),
    }
}

enum ApplyMsg {
    Progress { n: usize, current: String },
    Done(SyncReport),
}

enum Job {
    Compare {
        rx: Receiver<io::Result<Vec<SyncItem>>>,
        cancel: Arc<AtomicBool>,
        roots: (PathBuf, PathBuf),
    },
    Apply {
        rx: Receiver<ApplyMsg>,
        cancel: Arc<AtomicBool>,
        total: usize,
        n: usize,
        current: String,
    },
}

impl Job {
    fn cancel_flag(&self) -> &AtomicBool {
        match self {
            Job::Compare { cancel, .. } | Job::Apply { cancel, .. } => cancel,
        }
    }
}

pub struct SyncWindow {
    id: egui::Id,
    left: String,
    right: String,
    opts: SyncOptions,
    /// Roots the current `items` were computed from; apply and feed use these, not the fields.
    roots: (PathBuf, PathBuf),
    items: Vec<SyncItem>,
    visible: Vec<usize>,
    filter: Filter,
    job: Option<Job>,
    report: Option<SyncReport>,
    error: Option<String>,
    close: bool,
    picks: [FolderPick; 2],
}

impl SyncWindow {
    pub fn new(left: PathBuf, right: PathBuf) -> Self {
        Self {
            id: super::window_id("SyncWindow"),
            left: left.display().to_string(),
            right: right.display().to_string(),
            opts: SyncOptions::default(),
            roots: (left, right),
            items: Vec::new(),
            visible: Vec::new(),
            filter: Filter::default(),
            job: None,
            report: None,
            error: None,
            close: false,
            picks: Default::default(),
        }
    }

    fn refilter(&mut self) {
        self.visible = filter_indices(&self.items, &self.filter);
    }

    fn cancel_job(&self) {
        if let Some(job) = &self.job {
            job.cancel_flag().store(true, Ordering::Relaxed);
        }
    }

    fn start_compare(&mut self, ctx: &egui::Context) {
        let roots = (
            PathBuf::from(self.left.trim()),
            PathBuf::from(self.right.trim()),
        );
        let mut opts = self.opts.clone();
        if opts.mask.trim().is_empty() {
            opts.mask = "*".into();
        }
        let cancel = Arc::new(AtomicBool::new(false));
        let (tx, rx) = channel();
        let (l, r) = roots.clone();
        let (flag, ctx2) = (cancel.clone(), ctx.clone());
        std::thread::spawn(move || {
            let _ = tx.send(compare_dirs(&l, &r, &opts, &flag));
            ctx2.request_repaint();
        });
        self.error = None;
        self.job = Some(Job::Compare { rx, cancel, roots });
    }

    fn start_apply(&mut self, ctx: &egui::Context) {
        let items: Vec<SyncItem> = self
            .items
            .iter()
            .filter(|i| i.action != SyncAction::None)
            .cloned()
            .collect();
        let total = items.len();
        let cancel = Arc::new(AtomicBool::new(false));
        let (tx, rx) = channel();
        let (l, r) = self.roots.clone();
        let (flag, ctx2) = (cancel.clone(), ctx.clone());
        std::thread::spawn(move || {
            let mut n = 0;
            let report = apply_sync(&l, &r, &items, &flag, &mut |rel| {
                n += 1;
                let current = rel.display().to_string();
                let _ = tx.send(ApplyMsg::Progress { n, current });
                ctx2.request_repaint();
                true
            });
            let _ = tx.send(ApplyMsg::Done(report));
            ctx2.request_repaint();
        });
        self.error = None;
        self.report = None;
        self.job = Some(Job::Apply {
            rx,
            cancel,
            total,
            n: 0,
            current: String::new(),
        });
    }

    fn finish_compare(&mut self, res: io::Result<Vec<SyncItem>>, roots: (PathBuf, PathBuf)) {
        match res {
            Ok(items) => {
                self.items = items;
                self.roots = roots;
                self.refilter();
            }
            Err(e) if e.kind() == io::ErrorKind::Interrupted => {
                self.error = Some(t("Comparison cancelled", "Comparaison annulée").into());
            }
            Err(e) => self.error = Some(e.to_string()),
        }
    }

    /// Collects worker results. A finished apply reloads the panels and re-runs the compare.
    fn poll(&mut self, ctx: &egui::Context, shared: &Shared) {
        let mut compare_done = None;
        let mut apply_done = None;
        match &mut self.job {
            None => return,
            Some(Job::Compare { rx, roots, .. }) => match rx.try_recv() {
                Ok(res) => compare_done = Some((res, roots.clone())),
                Err(TryRecvError::Disconnected) => {
                    let e = io::Error::other("comparison stopped unexpectedly");
                    compare_done = Some((Err(e), roots.clone()));
                }
                Err(TryRecvError::Empty) => {}
            },
            Some(Job::Apply { rx, n, current, .. }) => loop {
                match rx.try_recv() {
                    Ok(ApplyMsg::Progress { n: i, current: c }) => {
                        *n = i;
                        *current = c;
                    }
                    Ok(ApplyMsg::Done(r)) => {
                        apply_done = Some(r);
                        break;
                    }
                    Err(TryRecvError::Empty) => break,
                    Err(TryRecvError::Disconnected) => {
                        apply_done = Some(SyncReport {
                            errors: vec!["synchronization stopped unexpectedly".into()],
                            ..SyncReport::default()
                        });
                        break;
                    }
                }
            },
        }
        if let Some((res, roots)) = compare_done {
            self.job = None;
            self.finish_compare(res, roots);
        }
        if let Some(report) = apply_done {
            self.job = None;
            self.report = Some(report);
            shared.send(AppEvent::Reload);
            self.start_compare(ctx);
        }
        if self.job.is_some() {
            ctx.request_repaint_after(Duration::from_millis(100));
        }
    }

    fn paths_ui(&mut self, ui: &mut egui::Ui) {
        let busy = self.job.is_some();
        // Plain rows, not a Grid: a grid cell is only as wide as last frame's column.
        ui.horizontal(|ui| {
            ui.add_sized([50.0, 20.0], egui::Label::new(t("Left", "Gauche")));
            let w = (ui.available_width() - 110.0).max(200.0);
            folder_field(ui, &mut self.left, &mut self.picks[0], w);
            let swap = ui.add_enabled(!busy, egui::Button::new(t("⇅ Swap", "⇅ Échanger")));
            if swap.clicked() {
                std::mem::swap(&mut self.left, &mut self.right);
            }
        });
        ui.horizontal(|ui| {
            ui.add_sized([50.0, 20.0], egui::Label::new(t("Right", "Droite")));
            let w = (ui.available_width() - 110.0).max(200.0);
            folder_field(ui, &mut self.right, &mut self.picks[1], w);
        });
    }

    fn options_ui(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        ui.horizontal_wrapped(|ui| {
            let o = &mut self.opts;
            ui.checkbox(&mut o.subdirs, t("Subfolders", "Sous-dossiers"));
            ui.checkbox(&mut o.by_content, t("By content", "Par contenu"));
            ui.checkbox(&mut o.ignore_date, t("Ignore date", "Ignorer la date"));
            let master = t(
                "Asymmetric (left is master)",
                "Asymétrique (gauche = maître)",
            );
            ui.checkbox(&mut o.asymmetric, master);
            ui.checkbox(&mut o.empty_dirs, t("Empty folders", "Dossiers vides"));
            ui.label(tr("mask"));
            ui.add(egui::TextEdit::singleline(&mut o.mask).desired_width(120.0));
        });
        ui.horizontal(|ui| match &self.job {
            Some(Job::Compare { .. }) => {
                ui.spinner();
                if ui.button(tr("cancel")).clicked() {
                    self.cancel_job();
                }
            }
            Some(Job::Apply { .. }) => {}
            None => {
                if ui.button(t("Compare", "Comparer")).clicked() {
                    self.report = None;
                    self.start_compare(ctx);
                }
            }
        });
    }

    fn filter_ui(&mut self, ui: &mut egui::Ui) {
        let n = count_classes(&self.items);
        let before = self.filter;
        ui.horizontal_wrapped(|ui| {
            let f = &mut self.filter;
            ui.checkbox(
                &mut f.equal,
                format!("= {} ({})", t("Equal", "Égaux"), n[0]),
            );
            let newer = t("Newer / different", "Plus récent / différent");
            ui.checkbox(&mut f.newer, format!("≠ {newer} ({})", n[1]));
            let lo = t("Left only", "Gauche seul");
            ui.checkbox(&mut f.left_only, format!("→ {lo} ({})", n[2]));
            let ro = t("Right only", "Droite seul");
            ui.checkbox(&mut f.right_only, format!("← {ro} ({})", n[3]));
        });
        if before != self.filter {
            self.refilter();
        }
    }

    fn table_ui(&mut self, ui: &mut egui::Ui, shared: &Shared) {
        let height = (ui.available_height() - 110.0).max(120.0);
        let mut double_clicked = None;
        let roots = self.roots.clone();
        let items = &mut self.items;
        let visible = &self.visible;
        let table = TableBuilder::new(ui)
            .id_salt("sync_table")
            .striped(true)
            .resizable(true)
            .sense(egui::Sense::click())
            .max_scroll_height(height)
            .column(Column::initial(240.0).at_least(80.0).clip(true))
            .column(Column::initial(80.0))
            .column(Column::initial(120.0))
            .column(Column::exact(28.0))
            .column(Column::initial(80.0))
            .column(Column::initial(120.0))
            .column(Column::initial(140.0))
            .column(Column::remainder().at_least(40.0));
        table
            .header(20.0, |mut h| {
                for title in header_titles() {
                    h.col(|ui| {
                        ui.strong(title);
                    });
                }
            })
            .body(|body| {
                body.rows(22.0, visible.len(), |mut row| {
                    let idx = visible[row.index()];
                    if let Some(rel) = sync_row(&mut row, idx, &mut items[idx]) {
                        double_clicked = Some(rel);
                    }
                });
            });
        if let Some(rel) = double_clicked {
            shared.send(AppEvent::CompareFiles(
                roots.0.join(&rel),
                roots.1.join(&rel),
            ));
        }
    }

    fn status_ui(&self, ui: &mut egui::Ui) {
        match &self.job {
            Some(Job::Apply {
                total, n, current, ..
            }) => {
                let frac = if *total == 0 {
                    0.0
                } else {
                    *n as f32 / *total as f32
                };
                ui.add(egui::ProgressBar::new(frac).text(format!("{n}/{total}  {current}")));
            }
            Some(Job::Compare { .. }) => {
                ui.label(t("Comparing…", "Comparaison…"));
            }
            None => {}
        }
        if let Some(e) = &self.error {
            ui.colored_label(ui.visuals().error_fg_color, format!("{}: {e}", tr("error")));
        }
        if let Some(r) = &self.report {
            ui.label(format!(
                "{}: {}   {}: {}   {}: {}",
                t("Copied", "Copiés"),
                r.copied,
                t("Deleted", "Supprimés"),
                r.deleted,
                t("Errors", "Erreurs"),
                r.errors.len()
            ));
            if !r.errors.is_empty() {
                egui::ScrollArea::vertical()
                    .id_salt("sync_errors")
                    .max_height(80.0)
                    .show(ui, |ui| {
                        for e in &r.errors {
                            ui.colored_label(ui.visuals().error_fg_color, e);
                        }
                    });
            }
        }
    }

    fn buttons_ui(&mut self, ui: &mut egui::Ui, ctx: &egui::Context, shared: &Shared) {
        ui.horizontal(|ui| {
            let idle = self.job.is_none();
            let pending = pending_count(&self.items);
            let label = format!("{} ({pending})", t("Synchronize", "Synchroniser"));
            if ui
                .add_enabled(idle && pending > 0, egui::Button::new(label))
                .clicked()
            {
                self.start_apply(ctx);
            }
            if matches!(self.job, Some(Job::Apply { .. })) && ui.button(tr("cancel")).clicked() {
                self.cancel_job();
            }
            let has_diff = self.items.iter().any(|i| i.status != SyncStatus::Equal);
            let feed = egui::Button::new(t("Feed to panel", "Envoyer au panneau"));
            if ui.add_enabled(idle && has_diff, feed).clicked() {
                shared.send(AppEvent::FeedToPanel {
                    title: tr("cmd.sync").into(),
                    paths: feed_paths(&self.items, &self.roots.0, &self.roots.1),
                });
            }
            if ui.button(tr("close")).clicked() {
                self.close = true;
            }
        });
    }
}

fn header_titles() -> [&'static str; 8] {
    [
        tr("path"),
        t("Left size", "Taille g."),
        t("Left date", "Date g."),
        "",
        t("Right size", "Taille d."),
        t("Right date", "Date d."),
        t("Action", "Action"),
        "",
    ]
}

/// Draws one difference; returns its relative path when a comparable pair was double-clicked.
fn sync_row(
    row: &mut egui_extras::TableRow<'_, '_>,
    idx: usize,
    item: &mut SyncItem,
) -> Option<PathBuf> {
    let (ls, ld) = side_text(item.left, item.is_dir);
    let (rs, rd) = side_text(item.right, item.is_dir);
    row.col(|ui| {
        let suffix = if item.is_dir { "/" } else { "" };
        ui.label(format!("{}{suffix}", item.rel.display()));
    });
    row.col(|ui| {
        ui.label(ls);
    });
    row.col(|ui| {
        ui.label(ld);
    });
    row.col(|ui| {
        let text = RichText::new(status_symbol(item.status))
            .color(status_color(item.status))
            .strong();
        ui.label(text);
    });
    row.col(|ui| {
        ui.label(rs);
    });
    row.col(|ui| {
        ui.label(rd);
    });
    row.col(|ui| action_combo(ui, idx, item));
    row.col(|ui| {
        ui.label(action_arrow(item.action));
    });
    let comparable = item.left.is_some()
        && item.right.is_some()
        && item.status != SyncStatus::Equal
        && !item.is_dir;
    (comparable && row.response().double_clicked()).then(|| item.rel.clone())
}

/// The per-row action combo, limited to the actions valid for the item's status.
fn action_combo(ui: &mut egui::Ui, idx: usize, item: &mut SyncItem) {
    egui::ComboBox::from_id_salt(("sync_action", idx))
        .width(120.0)
        .selected_text(action_label(item.action))
        .show_ui(ui, |ui| {
            for &a in valid_actions(item.status) {
                ui.selectable_value(&mut item.action, a, action_label(a));
            }
        });
}

impl Drop for SyncWindow {
    fn drop(&mut self) {
        self.cancel_job();
    }
}

impl ToolWindow for SyncWindow {
    fn show(&mut self, ctx: &egui::Context, shared: &mut Shared) -> bool {
        self.poll(ctx, shared);
        let mut open = true;
        super::tool_window(tr("cmd.sync"), ctx, [980.0, 600.0])
            .id(self.id)
            .open(&mut open)
            .resizable(true)
            .show(ctx, |ui| {
                self.paths_ui(ui);
                self.options_ui(ui, ctx);
                ui.separator();
                self.filter_ui(ui);
                self.table_ui(ui, shared);
                ui.separator();
                self.status_ui(ui);
                self.buttons_ui(ui, ctx, shared);
            });
        open && !self.close
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn item(rel: &str, status: SyncStatus, l: bool, r: bool) -> SyncItem {
        let side = Some(Side {
            size: 1,
            modified: None,
        });
        SyncItem {
            rel: rel.into(),
            is_dir: false,
            left: if l { side } else { None },
            right: if r { side } else { None },
            status,
            action: SyncAction::None,
        }
    }

    fn sample() -> Vec<SyncItem> {
        vec![
            item("eq", SyncStatus::Equal, true, true),
            item("ln", SyncStatus::LeftNewer, true, true),
            item("lo", SyncStatus::LeftOnly, true, false),
            item("ro", SyncStatus::RightOnly, false, true),
            item("df", SyncStatus::Different, true, true),
        ]
    }

    #[test]
    fn symbols() {
        assert_eq!(status_symbol(SyncStatus::Equal), "=");
        assert_eq!(status_symbol(SyncStatus::Different), "≠");
        assert_eq!(status_symbol(SyncStatus::LeftNewer), "→");
        assert_eq!(status_symbol(SyncStatus::LeftOnly), "→");
        assert_eq!(status_symbol(SyncStatus::RightNewer), "←");
        assert_eq!(status_symbol(SyncStatus::RightOnly), "←");
    }

    #[test]
    fn valid_actions_per_status() {
        use SyncAction::*;
        assert_eq!(valid_actions(SyncStatus::Equal), &[None]);
        assert_eq!(
            valid_actions(SyncStatus::LeftOnly),
            &[None, CopyToRight, DeleteLeft]
        );
        assert_eq!(
            valid_actions(SyncStatus::RightOnly),
            &[None, CopyToLeft, DeleteRight]
        );
        assert!(!valid_actions(SyncStatus::Different).contains(&DeleteLeft));
        assert!(valid_actions(SyncStatus::RightNewer).contains(&CopyToLeft));
    }

    #[test]
    fn default_actions_are_always_valid() {
        use SyncStatus::*;
        // The core's default action for each status must be offered by the combo.
        let defaults = [
            (LeftOnly, SyncAction::CopyToRight),
            (RightOnly, SyncAction::CopyToLeft),
            (LeftNewer, SyncAction::CopyToRight),
            (RightNewer, SyncAction::CopyToLeft),
        ];
        for (s, a) in defaults {
            assert!(valid_actions(s).contains(&a), "{s:?} {a:?}");
        }
    }

    #[test]
    fn filtering_by_class() {
        let items = sample();
        let mut f = Filter::default();
        assert_eq!(filter_indices(&items, &f), vec![1, 2, 3, 4]);
        f.equal = true;
        f.left_only = false;
        assert_eq!(filter_indices(&items, &f), vec![0, 1, 3, 4]);
        f.newer = false;
        f.right_only = false;
        assert_eq!(filter_indices(&items, &f), vec![0]);
    }

    #[test]
    fn counts_per_class() {
        assert_eq!(count_classes(&sample()), [1, 2, 1, 1]);
    }

    #[test]
    fn feed_uses_the_side_that_exists() {
        let paths = feed_paths(&sample(), Path::new("/L"), Path::new("/R"));
        assert_eq!(
            paths,
            vec![
                PathBuf::from("/L/ln"),
                PathBuf::from("/L/lo"),
                PathBuf::from("/R/ro"),
                PathBuf::from("/L/df"),
            ]
        );
    }

    #[test]
    fn pending_counts_non_skipped() {
        let mut items = sample();
        items[1].action = SyncAction::CopyToRight;
        items[3].action = SyncAction::DeleteRight;
        assert_eq!(pending_count(&items), 2);
    }
}
