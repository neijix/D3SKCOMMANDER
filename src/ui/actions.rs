//! Commands: what every menu item, button and shortcut does. Dispatch is in
//! `actions/dispatch.rs`, keyboard navigation in `actions/nav.rs`; the heavier tools
//! (pack, checksums, encryption …) are in `tools.rs`.

mod compare;
mod dispatch;
mod nav;

use std::path::{Path, PathBuf};

use eframe::egui;

use super::App;
use super::dialogs::{ConfirmAction, Dialog, InputAction};
use super::editor;
use super::lister;
use super::panel::Location;
use crate::config::HotlistItem;
use crate::core::checksum::Algo;
use crate::core::fileops::{OpKind, OpRequest};
use crate::core::fs::{self as cfs, Entry};
use crate::core::launcher::{self, LaunchMode};
use crate::i18n::{t, tr};

impl App {
    pub(crate) fn error(&mut self, text: impl Into<String>) {
        self.dialog = Some(Dialog::message(tr("error"), text));
    }

    pub(crate) fn targets(&self) -> Vec<Entry> {
        self.panels[self.active].tab().targets()
    }

    pub(crate) fn target_paths(&self) -> Vec<PathBuf> {
        self.panels[self.active].tab().target_paths()
    }

    /// Targets that are real files on disk (not inside an archive), or an error message.
    pub(crate) fn local_targets(&mut self) -> Option<Vec<PathBuf>> {
        if matches!(
            self.panels[self.active].tab().location,
            Location::Archive { .. }
        ) {
            self.error(t(
                "Not available inside an archive: unpack first (Alt+F9).",
                "Indisponible dans une archive : décompressez d'abord (Alt+F9).",
            ));
            return None;
        }
        let paths = self.target_paths();
        if paths.is_empty() {
            self.error(tr("dlg.no_selection"));
            return None;
        }
        Some(paths)
    }

    pub(crate) fn local_files(&mut self) -> Option<Vec<PathBuf>> {
        let files: Vec<PathBuf> = self
            .local_targets()?
            .into_iter()
            .filter(|p| p.is_file())
            .collect();
        if files.is_empty() {
            self.error(tr("dlg.no_selection"));
            return None;
        }
        Some(files)
    }

    pub(crate) fn add_hotlist(&mut self) {
        if let Some(dir) = self.active_dir() {
            let name = dir
                .file_name()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_else(|| dir.display().to_string());
            if !self.shared.settings.hotlist.iter().any(|h| h.path == dir) {
                self.shared
                    .settings
                    .hotlist
                    .push(HotlistItem { name, path: dir });
                self.status = tr("cmd.add_hotlist").into();
            }
        }
    }

    fn view_current(&mut self) {
        let Some(entry) = self.panels[self.active].tab().current().cloned() else {
            return;
        };
        if entry.is_parent {
            return;
        }
        if entry.is_dir {
            if matches!(self.panels[self.active].tab().location, Location::Local(_)) {
                self.compute_dir_sizes(vec![entry.path]);
            }
            return;
        }
        match self.local_copy(&entry) {
            Ok(p) => self.windows.push(Box::new(lister::ListerWindow::new(p))),
            Err(e) => self.error(e),
        }
    }

    fn edit_current(&mut self) {
        let Some(entry) = self.panels[self.active].tab().current().cloned() else {
            return;
        };
        if entry.is_dir || entry.is_parent {
            return;
        }
        if matches!(
            self.panels[self.active].tab().location,
            Location::Archive { .. }
        ) {
            return self.error(t(
                "Files inside an archive are read-only: unpack them first.",
                "Les fichiers d'une archive sont en lecture seule : décompressez-les d'abord.",
            ));
        }
        self.edit_file(entry.path);
    }

    /// F4: the configured external editor, else the built-in one.
    pub(crate) fn edit_file(&mut self, path: PathBuf) {
        let cmd = self.shared.settings.editor.trim().to_string();
        if cmd.is_empty() {
            self.windows.push(Box::new(editor::EditorWindow::new(path)));
            return;
        }
        let cwd = path
            .parent()
            .map(Path::to_path_buf)
            .unwrap_or_else(cfs::home_dir);
        if let Err(e) = launcher::launch(&cmd, &[path], LaunchMode::OncePerFile, &cwd) {
            self.error(e);
        }
    }

    fn copy_move(&mut self, kind: OpKind) {
        let loc = self.panels[self.active].tab().location.clone();
        let entries = self.targets();
        if entries.is_empty() {
            return self.error(tr("dlg.no_selection"));
        }
        let dest = self.other_dir().unwrap_or_default();
        if let Location::Archive { file, .. } = loc {
            if kind == OpKind::Move {
                return self.error(t(
                    "Moving out of an archive is not supported: copy (F5), then delete the archive.",
                    "Impossible de déplacer hors d'une archive : copiez (F5), puis supprimez l'archive.",
                ));
            }
            let inner = entries
                .iter()
                .map(|e| e.path.to_string_lossy().replace('\\', "/"))
                .collect();
            self.dialog = Some(Dialog::input(
                tr("cmd.unpack"),
                tr("dlg.unpack"),
                dest.display().to_string(),
                InputAction::Extract {
                    archive: file,
                    entries: Some(inner),
                },
            ));
            return;
        }
        if matches!(
            self.panels[1 - self.active].tab().location,
            Location::Archive { .. }
        ) {
            return self.error(t(
                "To add files to an archive, use Pack (Alt+F5).",
                "Pour ajouter des fichiers à une archive, utilisez Compresser (Alt+F5).",
            ));
        }
        let sources: Vec<PathBuf> = entries.iter().map(|e| e.path.clone()).collect();
        let label = format!(
            "{} {}",
            match sources.as_slice() {
                [one] => one
                    .file_name()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .into_owned(),
                many => format!("{} {}", many.len(), tr("files")),
            },
            if kind == OpKind::Copy {
                tr("dlg.copy_to")
            } else {
                tr("dlg.move_to")
            }
        );
        let mut text = dest.display().to_string();
        if !text.is_empty() && !text.ends_with(std::path::MAIN_SEPARATOR) {
            text.push(std::path::MAIN_SEPARATOR);
        }
        let title = if kind == OpKind::Copy {
            tr("cmd.copy")
        } else {
            tr("cmd.move")
        };
        self.dialog = Some(Dialog::input(
            title,
            label,
            text,
            InputAction::CopyMove { kind, sources },
        ));
    }

    fn rename_current(&mut self) {
        let tab = self.panels[self.active].tab();
        if !matches!(tab.location, Location::Local(_) | Location::List { .. }) {
            return;
        }
        if let Some(e) = tab.current().filter(|e| !e.is_parent).cloned() {
            self.dialog = Some(Dialog::input(
                tr("cmd.rename"),
                tr("dlg.rename"),
                e.name.clone(),
                InputAction::Rename { path: e.path },
            ));
        }
    }

    fn delete(&mut self, permanent: bool, ctx: &egui::Context) {
        let Some(paths) = self.local_targets() else {
            return;
        };
        let to_trash = self.shared.settings.use_trash && !permanent;
        let what = match paths.as_slice() {
            [one] => one.display().to_string(),
            many => format!("{} {}", many.len(), tr("files")),
        };
        let question = if to_trash {
            tr("dlg.delete")
        } else {
            tr("dlg.delete_perm")
        };
        let dialog = Dialog::Confirm {
            title: tr("cmd.delete").into(),
            text: format!("{question}\n{what}"),
            action: ConfirmAction::Delete { paths, to_trash },
        };
        if self.shared.settings.confirm_delete {
            self.dialog = Some(dialog);
        } else {
            self.run_dialog(dialog, ctx);
        }
    }

    fn properties(&mut self) {
        let Some(e) = self.panels[self.active]
            .tab()
            .current()
            .filter(|e| !e.is_parent)
            .cloned()
        else {
            return;
        };
        let size = match self.panels[self.active].tab().dir_sizes.get(&e.path) {
            Some(s) => *s,
            None => e.size,
        };
        let mut text = format!(
            "{}: {}\n{}: {} ({} B)\n{}: {}\n{}: {}",
            tr("path"),
            e.path.display(),
            tr("size"),
            cfs::human_size(size),
            cfs::grouped(size),
            tr("date"),
            cfs::format_time(e.modified),
            tr("attr"),
            e.attrs,
        );
        if e.is_link
            && let Ok(target) = std::fs::read_link(&e.path)
        {
            text.push_str(&format!("\n→ {}", target.display()));
        }
        if e.is_dir {
            text.push_str(&format!(
                "\n\n{}",
                t(
                    "Space or Ctrl+L computes a folder's size.",
                    "Espace ou Ctrl+L calcule la taille d'un dossier."
                )
            ));
        }
        self.dialog = Some(Dialog::message(&e.name, text));
    }

    fn open_terminal(&mut self) {
        let dir = self.active_dir().unwrap_or_else(cfs::home_dir);
        let custom = self.shared.settings.terminal.trim().to_string();
        let result = if custom.is_empty() {
            default_terminal(&dir)
        } else {
            launcher::run_in_shell(&custom, &dir)
        };
        if let Err(e) = result {
            self.error(e);
        }
    }

    pub(crate) fn start_file_op(&mut self, req: OpRequest, ctx: &egui::Context) {
        let title = match req.kind {
            OpKind::Copy => tr("copying"),
            OpKind::Move => tr("moving"),
            OpKind::Delete { .. } => tr("deleting"),
        };
        self.jobs.start_file_op(title.into(), req, ctx);
    }
}

/// Opens the platform's terminal in `dir`.
fn default_terminal(dir: &Path) -> Result<(), String> {
    use std::process::Command as Proc;
    let spawn = |cmd: &mut Proc| {
        cmd.current_dir(dir)
            .spawn()
            .map(|_| ())
            .map_err(|e| e.to_string())
    };
    if cfg!(windows) {
        spawn(Proc::new("cmd").args(["/c", "start", "cmd"]))
    } else if cfg!(target_os = "macos") {
        spawn(Proc::new("open").args(["-a", "Terminal"]).arg(dir))
    } else {
        let candidates = [
            "x-terminal-emulator",
            "gnome-terminal",
            "konsole",
            "xfce4-terminal",
            "alacritty",
            "kitty",
            "xterm",
        ];
        for c in candidates {
            if spawn(&mut Proc::new(c)).is_ok() {
                return Ok(());
            }
        }
        Err(t(
            "No terminal found: set one in the settings.",
            "Aucun terminal trouvé : choisissez-en un dans la configuration.",
        )
        .into())
    }
}

/// Default checksum output: `<name>.<ext>` for one file, `CHECKSUM.<ext>` for several.
pub(crate) fn checksum_out(
    files: &[PathBuf],
    base: &Path,
    algo: Algo,
    once_per_file: bool,
) -> String {
    if once_per_file {
        return String::new();
    }
    let name = match files {
        [one] => format!(
            "{}.{}",
            one.file_name().unwrap_or_default().to_string_lossy(),
            algo.extension()
        ),
        _ => format!("CHECKSUM.{}", algo.extension()),
    };
    base.join(name).display().to_string()
}
