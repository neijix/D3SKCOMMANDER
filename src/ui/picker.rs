//! The native "choose a folder" dialog (Windows, macOS, Linux portal or zenity), without
//! blocking the UI: the dialog is created on the UI thread (macOS requires it) and awaited
//! on a background thread; the answer is polled each frame.

use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::sync::mpsc::{Receiver, TryRecvError, channel};

use eframe::egui;

use crate::i18n::t;

/// Set by tests: every pick answers this at once instead of opening a native dialog.
static FAKE_ANSWER: Mutex<Option<Option<PathBuf>>> = Mutex::new(None);

/// Makes every later pick in this process answer `answer` (`None` = cancelled) without
/// opening a native dialog. For headless tests, which must never block on a real one.
pub fn answer_with(answer: Option<PathBuf>) {
    *FAKE_ANSWER.lock().unwrap_or_else(|e| e.into_inner()) = Some(answer);
}

/// One pending (or idle) folder pick.
#[derive(Default)]
pub struct FolderPick {
    rx: Option<Receiver<Option<PathBuf>>>,
}

impl FolderPick {
    /// True while the dialog is open.
    pub fn busy(&self) -> bool {
        self.rx.is_some()
    }

    /// Opens the dialog at `start` (when it is a folder). Ignored while one is open.
    pub fn start(&mut self, ctx: &egui::Context, start: Option<&Path>) {
        if self.busy() {
            return;
        }
        let (tx, rx) = channel();
        self.rx = Some(rx);
        let fake = FAKE_ANSWER
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .clone();
        if let Some(answer) = fake {
            let _ = tx.send(answer);
            ctx.request_repaint();
            return;
        }
        let mut dialog =
            rfd::AsyncFileDialog::new().set_title(t("Choose a folder", "Choisir un dossier"));
        if let Some(dir) = start.filter(|d| d.is_dir()) {
            dialog = dialog.set_directory(dir);
        }
        let pending = dialog.pick_folder();
        let ctx = ctx.clone();
        std::thread::spawn(move || {
            let answer = pollster::block_on(pending).map(|h| h.path().to_path_buf());
            let _ = tx.send(answer);
            ctx.request_repaint();
        });
    }

    /// The chosen folder, once, when the dialog closed with one.
    pub fn poll(&mut self) -> Option<PathBuf> {
        let rx = self.rx.as_ref()?;
        match rx.try_recv() {
            Ok(answer) => {
                self.rx = None;
                answer
            }
            Err(TryRecvError::Empty) => None,
            Err(TryRecvError::Disconnected) => {
                self.rx = None;
                None
            }
        }
    }
}

/// A one-line folder field with a browse button that fills it. Returns the text field's
/// response (callers may react to Enter in it).
pub fn folder_field(
    ui: &mut egui::Ui,
    text: &mut String,
    pick: &mut FolderPick,
    width: f32,
) -> egui::Response {
    if let Some(dir) = pick.poll() {
        *text = dir.display().to_string();
    }
    ui.horizontal(|ui| {
        let button_w = 28.0 + ui.spacing().item_spacing.x;
        let edit_w = (width.min(ui.available_width()) - button_w).max(60.0);
        let r = ui.add(egui::TextEdit::singleline(text).desired_width(edit_w));
        let browse = egui::Button::new("…").min_size(egui::vec2(28.0, 0.0));
        if ui
            .add_enabled(!pick.busy(), browse)
            .on_hover_text(t("Choose a folder…", "Choisir un dossier…"))
            .clicked()
        {
            let start = PathBuf::from(text.trim());
            pick.start(ui.ctx(), Some(&start));
        }
        r
    })
    .inner
}
