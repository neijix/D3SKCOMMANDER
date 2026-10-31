//! Background jobs: file operations (with the overwrite dialog) and generic tool jobs,
//! each shown in a progress window with a cancel button.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, Sender, channel};

use eframe::egui;

use crate::core::fileops::{self, ConflictChoice, FileInfo, OpEvent, OpRequest};
use crate::core::fs::{format_time, grouped, human_size};
use crate::i18n::tr;

/// Progress reporter handed to a generic job: `report(fraction, text)`; returns false
/// once the user cancelled.
pub struct Reporter {
    tx: Sender<JobMsg>,
    cancel: Arc<AtomicBool>,
    ctx: egui::Context,
}

impl Reporter {
    pub fn report(&self, fraction: f32, text: impl Into<String>) -> bool {
        let _ = self.tx.send(JobMsg::Progress(fraction, text.into()));
        self.ctx.request_repaint();
        !self.cancelled()
    }

    pub fn cancelled(&self) -> bool {
        self.cancel.load(Ordering::Relaxed)
    }

    pub fn cancel_flag(&self) -> &AtomicBool {
        &self.cancel
    }
}

enum JobMsg {
    Progress(f32, String),
    Done(Result<String, String>),
}

enum JobKind {
    FileOp(Receiver<OpEvent>),
    Generic(Receiver<JobMsg>),
}

struct Conflict {
    source: FileInfo,
    target: FileInfo,
    reply: Sender<ConflictChoice>,
}

pub struct Job {
    title: String,
    kind: JobKind,
    cancel: Arc<AtomicBool>,
    fraction: f32,
    text: String,
    conflict: Option<Conflict>,
    errors: Vec<String>,
    finished: Option<String>,
}

/// What a finished job tells the app.
pub struct JobOutcome {
    pub title: String,
    pub summary: String,
    pub errors: Vec<String>,
}

#[derive(Default)]
pub struct Jobs {
    jobs: Vec<Job>,
}

impl Jobs {
    pub fn is_empty(&self) -> bool {
        self.jobs.is_empty()
    }

    pub fn start_file_op(&mut self, title: String, req: OpRequest, ctx: &egui::Context) {
        let cancel = Arc::new(AtomicBool::new(false));
        let rx = fileops::spawn(req, cancel.clone());
        let (fwd_tx, fwd_rx) = channel();
        let ctx = ctx.clone();
        // Forward events so the UI repaints as soon as one arrives.
        std::thread::spawn(move || {
            for ev in rx {
                if fwd_tx.send(ev).is_err() {
                    break;
                }
                ctx.request_repaint();
            }
        });
        self.push(title, JobKind::FileOp(fwd_rx), cancel);
    }

    /// Runs `work` on a thread. Its `Ok` text is shown when it ends, `Err` as an error.
    pub fn start<F>(&mut self, title: String, ctx: &egui::Context, work: F)
    where
        F: FnOnce(&Reporter) -> Result<String, String> + Send + 'static,
    {
        let cancel = Arc::new(AtomicBool::new(false));
        let (tx, rx) = channel();
        let reporter = Reporter {
            tx: tx.clone(),
            cancel: cancel.clone(),
            ctx: ctx.clone(),
        };
        let ctx = ctx.clone();
        std::thread::spawn(move || {
            let result = work(&reporter);
            let _ = tx.send(JobMsg::Done(result));
            ctx.request_repaint();
        });
        self.push(title, JobKind::Generic(rx), cancel);
    }

    fn push(&mut self, title: String, kind: JobKind, cancel: Arc<AtomicBool>) {
        self.jobs.push(Job {
            title,
            kind,
            cancel,
            fraction: 0.0,
            text: tr("working").into(),
            conflict: None,
            errors: Vec::new(),
            finished: None,
        });
    }

    /// Drains events, draws the windows, and returns jobs that ended this frame.
    pub fn ui(&mut self, ctx: &egui::Context) -> Vec<JobOutcome> {
        let mut out = Vec::new();
        for (i, job) in self.jobs.iter_mut().enumerate() {
            job.poll();
            job.window(ctx, i);
        }
        self.jobs.retain_mut(|job| match job.finished.take() {
            Some(summary) => {
                out.push(JobOutcome {
                    title: job.title.clone(),
                    summary,
                    errors: std::mem::take(&mut job.errors),
                });
                false
            }
            None => true,
        });
        out
    }
}

impl Job {
    fn poll(&mut self) {
        match &self.kind {
            JobKind::FileOp(rx) => {
                let events: Vec<OpEvent> = rx.try_iter().collect();
                for ev in events {
                    self.on_op_event(ev);
                }
            }
            JobKind::Generic(rx) => {
                let msgs: Vec<JobMsg> = rx.try_iter().collect();
                for msg in msgs {
                    match msg {
                        JobMsg::Progress(f, t) => {
                            self.fraction = f;
                            self.text = t;
                        }
                        JobMsg::Done(Ok(s)) => self.finished = Some(s),
                        JobMsg::Done(Err(e)) => {
                            self.errors.push(e);
                            self.finished = Some(tr("error").into());
                        }
                    }
                }
            }
        }
    }

    fn on_op_event(&mut self, ev: OpEvent) {
        match ev {
            OpEvent::Progress(p) => {
                self.fraction = if p.total_bytes > 0 {
                    p.done_bytes as f32 / p.total_bytes as f32
                } else if p.total_files > 0 {
                    p.done_files as f32 / p.total_files as f32
                } else {
                    0.0
                };
                self.text = format!(
                    "{}/{} — {} / {}\n{}",
                    p.done_files,
                    p.total_files,
                    human_size(p.done_bytes),
                    human_size(p.total_bytes),
                    p.current.display()
                );
            }
            OpEvent::Conflict {
                source,
                target,
                reply,
            } => {
                self.conflict = Some(Conflict {
                    source,
                    target,
                    reply,
                })
            }
            OpEvent::Error { path, message } => {
                self.errors.push(format!("{}: {message}", path.display()))
            }
            OpEvent::Finished {
                done_files,
                errors,
                cancelled,
            } => {
                let mut s = format!("{done_files} {}", tr("files"));
                if errors > 0 {
                    s.push_str(&format!(", {errors} {}", tr("error").to_lowercase()));
                }
                if cancelled {
                    s.push_str(&format!(" ({})", tr("cancel").to_lowercase()));
                }
                self.finished = Some(s);
            }
        }
    }

    fn window(&mut self, ctx: &egui::Context, index: usize) {
        let mut answer = None;
        egui::Window::new(&self.title)
            .id(egui::Id::unique(("job", index)))
            .collapsible(false)
            .resizable(false)
            .default_width(460.0)
            .anchor(
                egui::Align2::CENTER_CENTER,
                egui::vec2(0.0, index as f32 * 24.0),
            )
            .show(ctx, |ui| {
                if let Some(c) = &self.conflict {
                    answer = conflict_ui(ui, c);
                    return;
                }
                ui.add(egui::ProgressBar::new(self.fraction).show_percentage());
                ui.label(&self.text);
                if !self.errors.is_empty() {
                    ui.colored_label(
                        ui.visuals().error_fg_color,
                        format!("{} {}", self.errors.len(), tr("error")),
                    );
                }
                if ui.button(tr("cancel")).clicked() {
                    self.cancel.store(true, Ordering::Relaxed);
                }
            });
        if let Some(choice) = answer
            && let Some(c) = self.conflict.take()
        {
            let _ = c.reply.send(choice);
        }
    }
}

/// The overwrite dialog: both files side by side, then every choice.
fn conflict_ui(ui: &mut egui::Ui, c: &Conflict) -> Option<ConflictChoice> {
    ui.heading(tr("dlg.overwrite"));
    ui.label(c.target.path.display().to_string());
    egui::Grid::new("conflict").striped(true).show(ui, |ui| {
        ui.label("");
        ui.strong(tr("size"));
        ui.strong(tr("date"));
        ui.end_row();
        for (label, info) in [
            (tr("dlg.source"), &c.source),
            (tr("dlg.existing"), &c.target),
        ] {
            ui.label(label);
            ui.label(grouped(info.size));
            ui.label(format_time(info.modified));
            ui.end_row();
        }
    });
    let newer = match (c.source.modified, c.target.modified) {
        (Some(s), Some(t)) if s > t => Some(tr("dlg.source")),
        (Some(s), Some(t)) if s < t => Some(tr("dlg.existing")),
        _ => None,
    };
    if let Some(n) = newer {
        ui.label(format!("↑ {n} ⏶"));
    }
    let mut choice = None;
    ui.horizontal_wrapped(|ui| {
        let buttons = [
            ("dlg.overwrite_btn", ConflictChoice::Overwrite),
            ("dlg.overwrite_all", ConflictChoice::OverwriteAll),
            ("dlg.overwrite_older", ConflictChoice::OverwriteAllOlder),
            ("dlg.skip", ConflictChoice::Skip),
            ("dlg.skip_all", ConflictChoice::SkipAll),
            ("dlg.rename_new", ConflictChoice::RenameNew),
            ("cancel", ConflictChoice::Cancel),
        ];
        for (key, value) in buttons {
            if ui.button(tr(key)).clicked() {
                choice = Some(value);
            }
        }
    });
    choice
}
