//! Modal dialogs: one at a time, drawn over the panels. A dialog only collects input;
//! what happens on OK lives in `actions.rs` / `tools.rs`.

use std::path::PathBuf;

use eframe::egui::{self, Key};

use super::App;
use super::keys;
use super::panel::Location;
use crate::core::archive::ArchiveKind;
use crate::core::checksum::Algo;
use crate::core::fileops::OpKind;
use crate::i18n::{t, tr};

/// What an input dialog's text is for.
pub enum InputAction {
    MkDir {
        dir: PathBuf,
    },
    NewFile {
        dir: PathBuf,
    },
    Rename {
        path: PathBuf,
    },
    Select {
        select: bool,
    },
    /// Copy / move the sources to the typed path.
    CopyMove {
        kind: OpKind,
        sources: Vec<PathBuf>,
    },
    /// Extract archive entries (`None` = all) to the typed folder.
    Extract {
        archive: PathBuf,
        entries: Option<Vec<String>>,
    },
    Combine {
        part: PathBuf,
    },
    Catalog {
        root: PathBuf,
    },
}

pub enum ConfirmAction {
    Delete { paths: Vec<PathBuf>, to_trash: bool },
}

pub struct PackForm {
    pub sources: Vec<PathBuf>,
    pub archive: String,
    pub kind: ArchiveKind,
    pub level: u8,
}

pub struct UnpackForm {
    pub archives: Vec<PathBuf>,
    pub dest: String,
}

pub struct SplitForm {
    pub file: PathBuf,
    pub dest: String,
    pub size: String,
}

pub struct ChecksumForm {
    pub files: Vec<PathBuf>,
    pub base: PathBuf,
    pub algo: Algo,
    pub once_per_file: bool,
    pub out: String,
}

/// AddTime (shift), ReDate (sequence) and Touch (fixed date) share one form.
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum TimeMode {
    Shift,
    Sequence,
    Fixed,
}

pub struct TimeForm {
    pub mode: TimeMode,
    pub files: Vec<PathBuf>,
    /// Offset (Shift), first date (Sequence) or date (Fixed).
    pub value: String,
    /// Step for Sequence.
    pub step: String,
    pub modified: bool,
    pub accessed: bool,
}

pub struct CryptForm {
    pub files: Vec<PathBuf>,
    pub encrypt: bool,
    pub password: String,
    pub confirm: String,
    pub dest: String,
}

pub struct OpenWithForm {
    pub files: Vec<PathBuf>,
    pub command: String,
    pub once_per_file: bool,
}

pub enum Dialog {
    Message {
        title: String,
        text: String,
    },
    Input {
        title: String,
        label: String,
        text: String,
        action: InputAction,
        focused: bool,
    },
    Confirm {
        title: String,
        text: String,
        action: ConfirmAction,
    },
    Pack(PackForm),
    Unpack(UnpackForm),
    Split(SplitForm),
    Checksum(ChecksumForm),
    Time(TimeForm),
    Crypt(CryptForm),
    OpenWith(OpenWithForm),
    Hotlist,
    History,
    Drives {
        side: usize,
    },
    Keys,
    About,
}

impl Dialog {
    pub fn message(title: impl Into<String>, text: impl Into<String>) -> Dialog {
        Dialog::Message {
            title: title.into(),
            text: text.into(),
        }
    }

    pub fn input(
        title: impl Into<String>,
        label: impl Into<String>,
        text: impl Into<String>,
        action: InputAction,
    ) -> Dialog {
        Dialog::Input {
            title: title.into(),
            label: label.into(),
            text: text.into(),
            action,
            focused: false,
        }
    }
}

/// What the dialog asks for after this frame.
enum Outcome {
    Keep(Dialog),
    Close,
    Run(Dialog),
    Go(PathBuf),
}

/// OK / Cancel row; Enter = OK, handled by the caller.
fn ok_cancel(ui: &mut egui::Ui, ok_enabled: bool) -> (bool, bool) {
    let mut ok = false;
    let mut cancel = false;
    ui.separator();
    ui.horizontal(|ui| {
        ok = ui
            .add_enabled(ok_enabled, egui::Button::new(tr("ok")))
            .clicked();
        cancel = ui.button(tr("cancel")).clicked();
    });
    (ok, cancel)
}

fn files_line(ui: &mut egui::Ui, files: &[PathBuf]) {
    let text = match files {
        [one] => one.display().to_string(),
        many => format!("{} {}", many.len(), tr("files")),
    };
    ui.label(text);
}

impl App {
    pub(crate) fn dialog_ui(&mut self, ctx: &egui::Context) {
        let Some(dialog) = self.dialog.take() else {
            return;
        };
        let enter = ctx.input(|i| i.key_pressed(Key::Enter));
        let mut outcome = Outcome::Keep(Dialog::About);
        let mut slot = Some(dialog);
        let modal = egui::Modal::new(egui::Id::unique("d3sk-dialog")).show(ctx, |ui| {
            ui.set_min_width(420.0);
            ui.set_max_width(640.0);
            if let Some(d) = slot.take() {
                outcome = self.dialog_body(ui, d, enter);
            }
        });
        if modal.should_close() && matches!(outcome, Outcome::Keep(_)) {
            outcome = Outcome::Close;
        }
        match outcome {
            Outcome::Keep(d) => self.dialog = Some(d),
            Outcome::Close => {}
            Outcome::Run(d) => self.run_dialog(d, ctx),
            Outcome::Go(dir) => {
                let hidden = self.show_hidden();
                self.active_panel()
                    .tab_mut()
                    .go(Location::Local(dir), hidden);
            }
        }
    }

    fn dialog_body(&mut self, ui: &mut egui::Ui, d: Dialog, enter: bool) -> Outcome {
        match d {
            Dialog::Message { title, text } => message_ui(ui, title, text, enter),
            Dialog::Input {
                title,
                label,
                text,
                action,
                focused,
            } => input_ui(ui, [title, label, text], action, focused, enter),
            Dialog::Confirm {
                title,
                text,
                action,
            } => confirm_ui(ui, title, text, action, enter),
            Dialog::Pack(f) => pack_ui(ui, f, enter),
            Dialog::Unpack(f) => unpack_ui(ui, f, enter),
            Dialog::Split(f) => split_ui(ui, f, enter),
            Dialog::Checksum(f) => checksum_ui(ui, f, enter),
            Dialog::Time(f) => time_ui(ui, f, enter),
            Dialog::Crypt(f) => crypt_ui(ui, f, enter),
            Dialog::OpenWith(f) => open_with_ui(ui, f, enter),
            Dialog::Hotlist => self.hotlist_ui(ui),
            Dialog::History => self.history_ui(ui),
            Dialog::Drives { side } => self.drives_ui(ui, side),
            Dialog::Keys => keys_ui(ui),
            Dialog::About => about_ui(ui),
        }
    }

    fn history_ui(&mut self, ui: &mut egui::Ui) -> Outcome {
        ui.heading(tr("cmd.history"));
        let mut go = None;
        egui::ScrollArea::vertical()
            .max_height(420.0)
            .show(ui, |ui| {
                for p in &self.shared.settings.history {
                    if ui
                        .selectable_label(false, p.display().to_string())
                        .clicked()
                    {
                        go = Some(p.clone());
                    }
                }
            });
        close_or(ui, go, Dialog::History)
    }

    fn drives_ui(&mut self, ui: &mut egui::Ui, side: usize) -> Outcome {
        ui.heading(tr("folder"));
        let mut go = None;
        let home = crate::core::fs::home_dir();
        if ui.button(format!("🏠 {}", home.display())).clicked() {
            go = Some(home);
        }
        for d in self.drives().to_vec() {
            let text = format!(
                "💾 {}  {}  ({} {})",
                d.path.display(),
                d.label,
                crate::core::fs::human_size(d.free),
                tr("free")
            );
            if ui.button(text).clicked() {
                go = Some(d.path.clone());
            }
        }
        if go.is_some() {
            self.active = side;
        }
        close_or(ui, go, Dialog::Drives { side })
    }

    fn hotlist_ui(&mut self, ui: &mut egui::Ui) -> Outcome {
        ui.heading(tr("cmd.hotlist"));
        let mut go = None;
        let mut remove = None;
        egui::ScrollArea::vertical()
            .max_height(420.0)
            .show(ui, |ui| {
                for (i, item) in self.shared.settings.hotlist.iter().enumerate() {
                    ui.horizontal(|ui| {
                        if ui
                            .selectable_label(false, format!("⭐ {}", item.name))
                            .on_hover_text(item.path.display().to_string())
                            .clicked()
                        {
                            go = Some(item.path.clone());
                        }
                        if ui.small_button("🗑").clicked() {
                            remove = Some(i);
                        }
                    });
                }
            });
        if let Some(i) = remove {
            self.shared.settings.hotlist.remove(i);
        }
        ui.separator();
        if ui.button(tr("cmd.add_hotlist")).clicked() {
            self.add_hotlist();
        }
        close_or(ui, go, Dialog::Hotlist)
    }
}

fn message_ui(ui: &mut egui::Ui, title: String, text: String, enter: bool) -> Outcome {
    ui.heading(&title);
    egui::ScrollArea::vertical()
        .max_height(400.0)
        .show(ui, |ui| ui.add(egui::Label::new(&text).selectable(true)));
    ui.separator();
    if ui.button(tr("ok")).clicked() || enter {
        Outcome::Close
    } else {
        Outcome::Keep(Dialog::Message { title, text })
    }
}

fn input_ui(
    ui: &mut egui::Ui,
    [title, label, mut text]: [String; 3],
    action: InputAction,
    focused: bool,
    enter: bool,
) -> Outcome {
    ui.heading(&title);
    ui.label(&label);
    let r = ui.add(egui::TextEdit::singleline(&mut text).desired_width(f32::INFINITY));
    if !focused {
        r.request_focus();
    }
    let (ok, cancel) = ok_cancel(ui, true);
    let d = Dialog::Input {
        title,
        label,
        text,
        action,
        focused: true,
    };
    decide(ok || enter, cancel, d)
}

fn confirm_ui(
    ui: &mut egui::Ui,
    title: String,
    text: String,
    action: ConfirmAction,
    enter: bool,
) -> Outcome {
    ui.heading(&title);
    ui.label(&text);
    let (ok, cancel) = ok_cancel(ui, true);
    let d = Dialog::Confirm {
        title,
        text,
        action,
    };
    decide(ok || enter, cancel, d)
}

fn unpack_ui(ui: &mut egui::Ui, mut f: UnpackForm, enter: bool) -> Outcome {
    ui.heading(tr("dlg.unpack"));
    files_line(ui, &f.archives);
    ui.label(tr("target"));
    ui.add(egui::TextEdit::singleline(&mut f.dest).desired_width(f32::INFINITY));
    let (ok, cancel) = ok_cancel(ui, !f.dest.trim().is_empty());
    decide(ok || enter, cancel, Dialog::Unpack(f))
}

fn open_with_ui(ui: &mut egui::Ui, mut f: OpenWithForm, enter: bool) -> Outcome {
    ui.heading(tr("cmd.open_with"));
    files_line(ui, &f.files);
    ui.label(tr("command"));
    ui.add(egui::TextEdit::singleline(&mut f.command).desired_width(f32::INFINITY));
    ui.weak(t(
        "%s = the selected files; without %s they are appended.",
        "%s = les fichiers sélectionnés ; sans %s ils sont ajoutés à la fin.",
    ));
    ui.horizontal(|ui| {
        ui.radio_value(&mut f.once_per_file, true, tr("dlg.once_per_file"));
        ui.radio_value(&mut f.once_per_file, false, tr("dlg.all_at_once"));
    });
    let (ok, cancel) = ok_cancel(ui, !f.command.trim().is_empty());
    decide(ok || enter, cancel, Dialog::OpenWith(f))
}

fn keys_ui(ui: &mut egui::Ui) -> Outcome {
    ui.heading(tr("cmd.keys"));
    egui::ScrollArea::vertical()
        .max_height(480.0)
        .show(ui, |ui| {
            egui::Grid::new("keys").striped(true).show(ui, |ui| {
                for (sc, cmd) in keys::bindings() {
                    ui.monospace(ui.ctx().format_shortcut(&sc));
                    ui.label(cmd.label());
                    ui.end_row();
                }
                for (k, what) in nav_help() {
                    ui.monospace(k);
                    ui.label(what);
                    ui.end_row();
                }
            });
        });
    close_or(ui, None, Dialog::Keys)
}

fn about_ui(ui: &mut egui::Ui) -> Outcome {
    ui.heading(format!("d3skcommander {}", env!("CARGO_PKG_VERSION")));
    ui.label(t(
        "A two-panel file manager for Linux, macOS and Windows.",
        "Un gestionnaire de fichiers à deux panneaux pour Linux, macOS et Windows.",
    ));
    ui.label(format!(
        "{}: {}",
        t("License", "Licence"),
        env!("CARGO_PKG_LICENSE")
    ));
    ui.label(crate::config::settings_path().display().to_string());
    close_or(ui, None, Dialog::About)
}

fn decide(ok: bool, cancel: bool, d: Dialog) -> Outcome {
    if cancel {
        Outcome::Close
    } else if ok {
        Outcome::Run(d)
    } else {
        Outcome::Keep(d)
    }
}

fn close_or(ui: &mut egui::Ui, go: Option<PathBuf>, d: Dialog) -> Outcome {
    ui.separator();
    if let Some(dir) = go {
        return Outcome::Go(dir);
    }
    if ui.button(tr("close")).clicked() {
        Outcome::Close
    } else {
        Outcome::Keep(d)
    }
}

fn nav_help() -> Vec<(&'static str, &'static str)> {
    vec![
        (
            "↑ ↓ PgUp PgDn Home End",
            t("Move the cursor", "Déplacer le curseur"),
        ),
        (
            "Shift + ↑ ↓",
            t("Select while moving", "Sélectionner en se déplaçant"),
        ),
        (
            "Insert / Space",
            t(
                "Select (Space on a folder: its size)",
                "Sélectionner (Espace sur un dossier : sa taille)",
            ),
        ),
        ("Enter", t("Open", "Ouvrir")),
        ("*", tr("cmd.invert")),
        (
            "a–z",
            t(
                "Jump to a name (type-ahead)",
                "Aller à un nom (saisie rapide)",
            ),
        ),
        (
            "Esc",
            t("Clear the quick filter", "Effacer le filtre rapide"),
        ),
    ]
}

fn pack_ui(ui: &mut egui::Ui, mut f: PackForm, enter: bool) -> Outcome {
    ui.heading(tr("dlg.pack"));
    files_line(ui, &f.sources);
    ui.label(tr("target"));
    ui.add(egui::TextEdit::singleline(&mut f.archive).desired_width(f32::INFINITY));
    ui.horizontal(|ui| {
        ui.label(tr("dlg.format"));
        for kind in ArchiveKind::creatable() {
            if ui.radio_value(&mut f.kind, *kind, kind.label()).changed() {
                f.archive = with_archive_ext(&f.archive, *kind);
            }
        }
    });
    if matches!(
        f.kind,
        ArchiveKind::Zip | ArchiveKind::TarGz | ArchiveKind::SevenZ
    ) {
        ui.add(egui::Slider::new(&mut f.level, 0..=9).text(tr("dlg.level")));
    }
    let (ok, cancel) = ok_cancel(ui, !f.archive.trim().is_empty());
    decide(ok || enter, cancel, Dialog::Pack(f))
}

/// Replaces a known archive extension by `kind`'s.
pub fn with_archive_ext(path: &str, kind: ArchiveKind) -> String {
    let lower = path.to_lowercase();
    let known = [".tar.gz", ".tgz", ".zip", ".tar", ".7z"];
    let stem = known
        .iter()
        .find(|e| lower.ends_with(*e))
        .map(|e| &path[..path.len() - e.len()])
        .unwrap_or(path);
    format!("{stem}.{}", kind.extension())
}

fn split_ui(ui: &mut egui::Ui, mut f: SplitForm, enter: bool) -> Outcome {
    ui.heading(tr("cmd.split"));
    ui.label(f.file.display().to_string());
    ui.label(tr("target"));
    ui.add(egui::TextEdit::singleline(&mut f.dest).desired_width(f32::INFINITY));
    ui.horizontal(|ui| {
        ui.label(tr("dlg.part_size"));
        ui.text_edit_singleline(&mut f.size);
    });
    ui.horizontal_wrapped(|ui| {
        for preset in ["1.44M", "10M", "100M", "650M", "700M", "4095M"] {
            if ui.small_button(preset).clicked() {
                f.size = preset.into();
            }
        }
    });
    let valid = crate::core::splitter::parse_size(&f.size).is_ok_and(|n| n > 0);
    let (ok, cancel) = ok_cancel(ui, valid && !f.dest.trim().is_empty());
    decide(ok || (enter && valid), cancel, Dialog::Split(f))
}

fn checksum_ui(ui: &mut egui::Ui, mut f: ChecksumForm, enter: bool) -> Outcome {
    ui.heading(tr("cmd.checksum_create"));
    files_line(ui, &f.files);
    ui.horizontal(|ui| {
        ui.label(tr("dlg.algo"));
        for algo in Algo::all() {
            if ui.radio_value(&mut f.algo, *algo, algo.label()).changed() {
                f.out = super::actions::checksum_out(&f.files, &f.base, f.algo, f.once_per_file);
            }
        }
    });
    if f.files.len() > 1
        && ui
            .checkbox(
                &mut f.once_per_file,
                t("One file per file", "Un fichier par fichier"),
            )
            .changed()
    {
        f.out = super::actions::checksum_out(&f.files, &f.base, f.algo, f.once_per_file);
    }
    if !f.once_per_file {
        ui.label(tr("dlg.out_file"));
        ui.add(egui::TextEdit::singleline(&mut f.out).desired_width(f32::INFINITY));
    }
    let (ok, cancel) = ok_cancel(ui, true);
    decide(ok || enter, cancel, Dialog::Checksum(f))
}

fn time_ui(ui: &mut egui::Ui, mut f: TimeForm, enter: bool) -> Outcome {
    let (title, label) = match f.mode {
        TimeMode::Shift => (tr("cmd.addtime"), tr("dlg.offset")),
        TimeMode::Sequence => (tr("cmd.redate"), tr("dlg.start_date")),
        TimeMode::Fixed => (tr("cmd.touch"), tr("date")),
    };
    ui.heading(title);
    files_line(ui, &f.files);
    ui.label(label);
    ui.text_edit_singleline(&mut f.value);
    if f.mode == TimeMode::Sequence {
        ui.label(tr("dlg.step"));
        ui.text_edit_singleline(&mut f.step);
    } else {
        ui.checkbox(&mut f.modified, tr("dlg.modified"));
        ui.checkbox(&mut f.accessed, tr("dlg.accessed"));
    }
    let (ok, cancel) = ok_cancel(ui, !f.value.trim().is_empty());
    decide(ok || enter, cancel, Dialog::Time(f))
}

fn crypt_ui(ui: &mut egui::Ui, mut f: CryptForm, enter: bool) -> Outcome {
    ui.heading(if f.encrypt {
        tr("cmd.encrypt")
    } else {
        tr("cmd.decrypt")
    });
    files_line(ui, &f.files);
    ui.label(tr("password"));
    ui.add(egui::TextEdit::singleline(&mut f.password).password(true));
    let mut valid = !f.password.is_empty();
    if f.encrypt {
        ui.label(tr("confirm_password"));
        ui.add(egui::TextEdit::singleline(&mut f.confirm).password(true));
        if !f.confirm.is_empty() && f.confirm != f.password {
            ui.colored_label(
                ui.visuals().error_fg_color,
                t("The passwords differ.", "Les mots de passe diffèrent."),
            );
        }
        valid &= f.confirm == f.password;
        ui.weak(t(
            "age format (scrypt + ChaCha20-Poly1305). A lost password cannot be recovered.",
            "Format age (scrypt + ChaCha20-Poly1305). Un mot de passe perdu est irrécupérable.",
        ));
    }
    ui.label(tr("target"));
    ui.add(egui::TextEdit::singleline(&mut f.dest).desired_width(f32::INFINITY));
    let (ok, cancel) = ok_cancel(ui, valid);
    decide((ok || enter) && valid, cancel, Dialog::Crypt(f))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn archive_extension_is_replaced() {
        assert_eq!(with_archive_ext("/t/a.zip", ArchiveKind::SevenZ), "/t/a.7z");
        assert_eq!(
            with_archive_ext("/t/a.tar.gz", ArchiveKind::Zip),
            "/t/a.zip"
        );
        assert_eq!(with_archive_ext("/t/a", ArchiveKind::Tar), "/t/a.tar");
    }
}
