//! The built-in add-ons (packers, split/combine, checksums, AddTime/ReDate, disk catalog,
//! encryption, backup) and what every dialog does on OK.

use std::path::{Path, PathBuf};
use std::sync::atomic::AtomicBool;
use std::time::SystemTime;

use eframe::egui;

use super::dialogs::{
    ChecksumForm, CryptForm, Dialog, InputAction, PackForm, SplitForm, TimeForm, TimeMode,
    UnpackForm,
};
use super::panel::Location;
use super::{App, AppEvent};
use crate::core::archive::{self, ArchiveKind};
use crate::core::checksum::{self, Algo, Verify};
use crate::core::fs as cfs;
use crate::core::launcher::{self, LaunchMode};
use crate::i18n::{t, tr};

mod run;

impl App {
    /// Folder sizes, computed on threads and reported as `AppEvent::DirSize`.
    pub(crate) fn compute_dir_sizes(&mut self, dirs: Vec<PathBuf>) {
        if dirs.is_empty() {
            return;
        }
        let tx = self.shared.tx.clone();
        let ctx = self.ctx.clone();
        std::thread::spawn(move || {
            let cancel = AtomicBool::new(false);
            for d in dirs {
                let (bytes, _, _) = cfs::dir_size(&d, &cancel);
                if tx.send(AppEvent::DirSize(d, bytes)).is_err() {
                    break;
                }
                if let Some(c) = &ctx {
                    c.request_repaint();
                }
            }
        });
    }

    pub(crate) fn pack_dialog(&mut self) {
        let Some(sources) = self.local_targets() else {
            return;
        };
        let dest = self
            .other_dir()
            .or_else(|| self.active_dir())
            .unwrap_or_default();
        let name = match sources.as_slice() {
            [one] => one
                .file_stem()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_else(|| "archive".into()),
            _ => self
                .active_dir()
                .and_then(|d| d.file_name().map(|n| n.to_string_lossy().into_owned()))
                .unwrap_or_else(|| "archive".into()),
        };
        self.dialog = Some(Dialog::Pack(PackForm {
            sources,
            archive: dest.join(format!("{name}.zip")).display().to_string(),
            kind: ArchiveKind::Zip,
            level: 6,
        }));
    }

    pub(crate) fn unpack_dialog(&mut self) {
        let loc = self.panels[self.active].tab().location.clone();
        let dest = self.other_dir().unwrap_or_default().display().to_string();
        if let Location::Archive { file, .. } = loc {
            let entries: Vec<String> = self
                .targets()
                .iter()
                .map(|e| e.path.to_string_lossy().replace('\\', "/"))
                .collect();
            let all = self.panels[self.active].tab().selected.is_empty();
            self.dialog = Some(Dialog::input(
                tr("cmd.unpack"),
                tr("dlg.unpack"),
                dest,
                InputAction::Extract {
                    archive: file,
                    entries: (!all).then_some(entries),
                },
            ));
            return;
        }
        let archives: Vec<PathBuf> = self
            .target_paths()
            .into_iter()
            .filter(|p| p.is_file() && ArchiveKind::from_path(p).is_some())
            .collect();
        if archives.is_empty() {
            return self.error(t(
                "Select one or more archives.",
                "Sélectionnez une ou plusieurs archives.",
            ));
        }
        self.dialog = Some(Dialog::Unpack(UnpackForm { archives, dest }));
    }

    /// Backup to ZIP: the selection, zipped into the other panel with a time stamp.
    pub(crate) fn backup_zip(&mut self, ctx: &egui::Context) {
        let Some(sources) = self.local_targets() else {
            return;
        };
        let Some(dest_dir) = self.other_dir().or_else(|| self.active_dir()) else {
            return;
        };
        let base = match sources.as_slice() {
            [one] => one.file_name().map(|n| n.to_string_lossy().into_owned()),
            _ => self
                .active_dir()
                .and_then(|d| d.file_name().map(|n| n.to_string_lossy().into_owned())),
        }
        .unwrap_or_else(|| "backup".into());
        let stamp = chrono::Local::now().format("%Y%m%d-%H%M%S");
        let out = dest_dir.join(format!("{base}-backup-{stamp}.zip"));
        self.start_pack(sources, out, ArchiveKind::Zip, 6, ctx);
    }

    fn start_pack(
        &mut self,
        sources: Vec<PathBuf>,
        out: PathBuf,
        kind: ArchiveKind,
        level: u8,
        ctx: &egui::Context,
    ) {
        let title = format!("{} → {}", tr("cmd.pack"), out.display());
        self.jobs.start(title, ctx, move |r| {
            let n = archive::create(&out, kind, &sources, level, &mut |name| r.report(0.5, name))
                .map_err(|e| e.to_string())?;
            Ok(format!("{n} {} → {}", tr("files"), out.display()))
        });
    }

    fn start_extract(
        &mut self,
        archive_path: PathBuf,
        entries: Option<Vec<String>>,
        dest: PathBuf,
        ctx: &egui::Context,
    ) {
        let title = format!("{} {}", tr("cmd.unpack"), archive_path.display());
        self.jobs.start(title, ctx, move |r| {
            std::fs::create_dir_all(&dest).map_err(|e| e.to_string())?;
            let n = archive::extract(&archive_path, entries.as_deref(), &dest, &mut |name| {
                r.report(0.5, name)
            })
            .map_err(|e| e.to_string())?;
            Ok(format!("{n} {} → {}", tr("files"), dest.display()))
        });
    }

    pub(crate) fn split_dialog(&mut self) {
        let Some(files) = self.local_files() else {
            return;
        };
        let dest = self.other_dir().unwrap_or_default().display().to_string();
        self.dialog = Some(Dialog::Split(SplitForm {
            file: files[0].clone(),
            dest,
            size: "100M".into(),
        }));
    }

    pub(crate) fn combine_dialog(&mut self) {
        let Some(files) = self.local_files() else {
            return;
        };
        let dest = self.other_dir().unwrap_or_default().display().to_string();
        self.dialog = Some(Dialog::input(
            tr("cmd.combine"),
            tr("target"),
            dest,
            InputAction::Combine {
                part: files[0].clone(),
            },
        ));
    }

    pub(crate) fn checksum_dialog(&mut self) {
        let Some(paths) = self.local_targets() else {
            return;
        };
        let files = expand_files(&paths);
        if files.is_empty() {
            return self.error(tr("dlg.no_selection"));
        }
        let base = self.active_dir().unwrap_or_default();
        let algo = Algo::Sha256;
        let out = super::actions::checksum_out(&files, &base, algo, false);
        self.dialog = Some(Dialog::Checksum(ChecksumForm {
            files,
            base,
            algo,
            once_per_file: false,
            out,
        }));
    }

    pub(crate) fn checksum_verify(&mut self, ctx: &egui::Context) {
        let Some(files) = self.local_files() else {
            return;
        };
        let lists: Vec<PathBuf> = files
            .into_iter()
            .filter(|p| {
                let ext = p
                    .extension()
                    .map(|e| e.to_string_lossy().to_lowercase())
                    .unwrap_or_default();
                Algo::all().iter().any(|a| a.extension() == ext)
            })
            .collect();
        if lists.is_empty() {
            return self.error(t(
                "Select a checksum file (.sfv, .md5, .sha, .sha256).",
                "Sélectionnez un fichier de sommes (.sfv, .md5, .sha, .sha256).",
            ));
        }
        let tx = self.shared.tx.clone();
        self.jobs
            .start(tr("cmd.checksum_verify").into(), ctx, move |r| {
                let mut report = String::new();
                let (mut ok, mut bad) = (0, 0);
                for list in &lists {
                    r.report(0.5, list.display().to_string());
                    let results = checksum::verify_checksum_file(list, r.cancel_flag())
                        .map_err(|e| e.to_string())?;
                    for (name, v) in results {
                        let line = match v {
                            Verify::Ok => {
                                ok += 1;
                                continue;
                            }
                            Verify::Mismatch => {
                                format!("✗ {name}: {}", t("checksum differs", "somme différente"))
                            }
                            Verify::Missing => format!("? {name}: {}", t("missing", "absent")),
                            Verify::Error(e) => format!("! {name}: {e}"),
                        };
                        bad += 1;
                        report.push_str(&line);
                        report.push('\n');
                    }
                }
                let summary = format!("{ok} OK, {bad} {}", t("problem(s)", "problème(s)"));
                let _ = tx.send(AppEvent::Error(format!("{summary}\n\n{report}")));
                Ok(summary)
            });
    }

    pub(crate) fn time_dialog(&mut self, mode: TimeMode) {
        let Some(files) = self.local_targets() else {
            return;
        };
        let value = match mode {
            TimeMode::Shift => "+1h".to_string(),
            _ => chrono::Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
        };
        self.dialog = Some(Dialog::Time(TimeForm {
            mode,
            files,
            value,
            step: "60s".into(),
            modified: true,
            accessed: false,
        }));
    }

    pub(crate) fn catalog_dialog(&mut self) {
        let Some(root) = self.active_dir() else {
            return;
        };
        let name = root
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| "disk".into());
        let out = self
            .other_dir()
            .unwrap_or_else(|| root.clone())
            .join(format!("{name}.cat"));
        self.dialog = Some(Dialog::input(
            tr("cmd.catalog"),
            tr("dlg.out_file"),
            out.display().to_string(),
            InputAction::Catalog { root },
        ));
    }

    pub(crate) fn crypt_dialog(&mut self, encrypt: bool) {
        let Some(files) = self.local_files() else {
            return;
        };
        let dest = files[0]
            .parent()
            .map(Path::to_path_buf)
            .unwrap_or_default()
            .display()
            .to_string();
        self.dialog = Some(Dialog::Crypt(CryptForm {
            files,
            encrypt,
            password: String::new(),
            confirm: String::new(),
            dest,
        }));
    }

    /// Runs one of the user commands from the settings on the selection (lst2multi-style).
    pub(crate) fn run_user_command(&mut self, index: usize) {
        let Some(uc) = self.shared.settings.user_commands.get(index).cloned() else {
            return;
        };
        let files = self.target_paths();
        let mode = if uc.once_per_file {
            LaunchMode::OncePerFile
        } else {
            LaunchMode::AllAtOnce
        };
        let cwd = self.active_dir().unwrap_or_else(cfs::home_dir);
        match launcher::launch(&uc.command, &files, mode, &cwd) {
            Ok(n) => self.status = format!("{}: {n}", uc.name),
            Err(e) => self.error(e),
        }
    }
}

fn fraction(done: u64, total: u64) -> f32 {
    if total == 0 {
        0.0
    } else {
        done as f32 / total as f32
    }
}

/// The selection with folders replaced by every file inside them.
fn expand_files(paths: &[PathBuf]) -> Vec<PathBuf> {
    let mut out = Vec::new();
    for p in paths {
        if p.is_dir() {
            out.extend(
                walkdir::WalkDir::new(p)
                    .into_iter()
                    .filter_map(Result::ok)
                    .filter(|e| e.file_type().is_file())
                    .map(|e| e.into_path()),
            );
        } else if p.is_file() {
            out.push(p.clone());
        }
    }
    out
}

/// `YYYY-MM-DD`, `YYYY-MM-DD HH:MM` or `YYYY-MM-DD HH:MM:SS`, local time.
pub(crate) fn parse_date(s: &str) -> Result<SystemTime, String> {
    use chrono::{Local, NaiveDate, NaiveDateTime, TimeZone};
    let s = s.trim();
    let naive = NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
        .or_else(|_| NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M"))
        .or_else(|_| {
            NaiveDate::parse_from_str(s, "%Y-%m-%d")
                .map(|d| d.and_hms_opt(0, 0, 0).unwrap_or_default())
        })
        .map_err(|_| {
            format!(
                "{}: {s} (YYYY-MM-DD HH:MM:SS)",
                t("Invalid date", "Date invalide")
            )
        })?;
    Local
        .from_local_datetime(&naive)
        .earliest()
        .map(SystemTime::from)
        .ok_or_else(|| format!("{}: {s}", t("Invalid date", "Date invalide")))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dates_parse_in_three_forms() {
        assert!(parse_date("2024-02-29").is_ok());
        assert!(parse_date("2024-02-29 13:45").is_ok());
        assert!(parse_date("2024-02-29 13:45:10").is_ok());
        assert!(parse_date("29/02/2024").is_err());
        let a = parse_date("2024-01-01 00:00:10").unwrap();
        let b = parse_date("2024-01-01").unwrap();
        assert_eq!(a.duration_since(b).unwrap().as_secs(), 10);
    }

    #[test]
    fn folders_expand_to_their_files() {
        let dir = tempfile::tempdir().unwrap();
        std::fs::create_dir(dir.path().join("sub")).unwrap();
        std::fs::write(dir.path().join("sub/a.txt"), "a").unwrap();
        std::fs::write(dir.path().join("b.txt"), "b").unwrap();
        let mut files = expand_files(&[dir.path().join("sub"), dir.path().join("b.txt")]);
        files.sort();
        assert_eq!(files.len(), 2);
    }
}
