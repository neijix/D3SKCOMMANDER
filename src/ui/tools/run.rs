//! What every dialog does once the user pressed OK.

use std::path::{Path, PathBuf};

use eframe::egui;

use super::{fraction, parse_date};
use crate::core::checksum;
use crate::core::fileops::{OpKind, OpRequest};
use crate::core::fs as cfs;
use crate::core::launcher::{self, LaunchMode};
use crate::core::{catalog, crypt, splitter, timestamps};
use crate::i18n::{t, tr};
use crate::ui::App;
use crate::ui::dialogs::{
    ChecksumForm, ConfirmAction, CryptForm, Dialog, InputAction, OpenWithForm, PackForm, SplitForm,
    TimeForm, TimeMode, UnpackForm,
};

impl App {
    /// What a dialog does once the user pressed OK.
    pub(crate) fn run_dialog(&mut self, d: Dialog, ctx: &egui::Context) {
        match d {
            Dialog::Input { text, action, .. } => self.run_input(text, action, ctx),
            Dialog::Confirm {
                action: ConfirmAction::Delete { paths, to_trash },
                ..
            } => {
                let req = OpRequest {
                    kind: OpKind::Delete { to_trash },
                    sources: paths,
                    dest_dir: None,
                    rename_to: None,
                };
                self.start_file_op(req, ctx);
            }
            Dialog::Pack(f) => self.run_pack(f, ctx),
            Dialog::Unpack(f) => self.run_unpack(f, ctx),
            Dialog::Split(f) => self.run_split(f, ctx),
            Dialog::Checksum(f) => self.run_checksum(f, ctx),
            Dialog::Time(f) => self.run_time(f),
            Dialog::Crypt(f) => self.run_crypt(f, ctx),
            Dialog::OpenWith(f) => self.run_open_with(f),
            Dialog::Message { .. }
            | Dialog::Hotlist
            | Dialog::History
            | Dialog::Drives { .. }
            | Dialog::Keys
            | Dialog::About => {}
        }
    }

    fn run_pack(&mut self, f: PackForm, ctx: &egui::Context) {
        let out = PathBuf::from(f.archive.trim());
        if out.exists() {
            return self.error(format!(
                "{}: {}",
                t("Already exists", "Existe déjà"),
                out.display()
            ));
        }
        self.start_pack(f.sources, out, f.kind, f.level, ctx);
    }

    fn run_unpack(&mut self, f: UnpackForm, ctx: &egui::Context) {
        let dest = PathBuf::from(f.dest.trim());
        for a in f.archives {
            self.start_extract(a, None, dest.clone(), ctx);
        }
    }

    fn run_split(&mut self, f: SplitForm, ctx: &egui::Context) {
        let size = splitter::parse_size(&f.size).unwrap_or(0);
        let dest = PathBuf::from(f.dest.trim());
        let file = f.file;
        self.jobs.start(tr("cmd.split").into(), ctx, move |r| {
            std::fs::create_dir_all(&dest).map_err(|e| e.to_string())?;
            let parts = splitter::split_file(&file, &dest, size, &mut |done, total| {
                r.report(fraction(done, total), cfs::human_size(done))
            })
            .map_err(|e| e.to_string())?;
            Ok(format!("{} {}", parts.len(), t("parts", "parties")))
        });
    }

    fn run_checksum(&mut self, f: ChecksumForm, ctx: &egui::Context) {
        let ChecksumForm {
            files,
            base,
            algo,
            once_per_file,
            out,
        } = f;
        self.jobs
            .start(tr("cmd.checksum_create").into(), ctx, move |r| {
                let mut n = 0;
                if once_per_file {
                    for file in &files {
                        r.report(0.5, file.display().to_string());
                        let dir = file.parent().unwrap_or(&base);
                        let out = PathBuf::from(crate::ui::actions::checksum_out(
                            std::slice::from_ref(file),
                            dir,
                            algo,
                            false,
                        ));
                        n += checksum::write_checksum_file(
                            std::slice::from_ref(file),
                            dir,
                            algo,
                            &out,
                            r.cancel_flag(),
                        )
                        .map_err(|e| e.to_string())?;
                    }
                } else {
                    r.report(0.5, out.clone());
                    n = checksum::write_checksum_file(
                        &files,
                        &base,
                        algo,
                        Path::new(out.trim()),
                        r.cancel_flag(),
                    )
                    .map_err(|e| e.to_string())?;
                }
                Ok(format!("{n} {}", tr("files")))
            });
    }

    fn run_open_with(&mut self, f: OpenWithForm) {
        let mode = if f.once_per_file {
            LaunchMode::OncePerFile
        } else {
            LaunchMode::AllAtOnce
        };
        let cwd = self.active_dir().unwrap_or_else(cfs::home_dir);
        if let Err(e) = launcher::launch(&f.command, &f.files, mode, &cwd) {
            self.error(e);
        }
    }

    fn run_input(&mut self, text: String, action: InputAction, ctx: &egui::Context) {
        let value = text.trim().to_string();
        match action {
            InputAction::MkDir { dir } => self.run_mkdir(&dir, &value),
            InputAction::NewFile { dir } => self.run_new_file(&dir, &value),
            InputAction::Rename { path } => self.run_rename(&path, &value),
            InputAction::Select { select } => {
                self.active_panel()
                    .tab_mut()
                    .select_mask(&value, select, false);
            }
            InputAction::CopyMove { kind, sources } => {
                self.run_copy_move(kind, sources, &value, ctx)
            }
            InputAction::Extract { archive, entries } => {
                self.start_extract(archive, entries, PathBuf::from(value), ctx);
            }
            InputAction::Combine { part } => self.run_combine(part, value, ctx),
            InputAction::Catalog { root } => self.run_catalog(root, value, ctx),
        }
    }

    fn run_mkdir(&mut self, dir: &Path, value: &str) {
        if value.is_empty() {
            return;
        }
        let path = dir.join(value);
        match std::fs::create_dir_all(&path) {
            Ok(()) => {
                self.reload_all();
                let first = dir.join(
                    Path::new(value)
                        .components()
                        .next()
                        .map(|c| c.as_os_str())
                        .unwrap_or_default(),
                );
                self.active_panel().tab_mut().select_path(&first);
            }
            Err(e) => self.error(format!("{}: {e}", path.display())),
        }
    }

    fn run_new_file(&mut self, dir: &Path, value: &str) {
        if let Err(e) = cfs::validate_name(value) {
            return self.error(e);
        }
        let path = dir.join(value);
        if !path.exists()
            && let Err(e) = cfs::create_file(&path)
        {
            return self.error(format!("{}: {e}", path.display()));
        }
        self.reload_all();
        self.active_panel().tab_mut().select_path(&path);
        self.edit_file(path);
    }

    fn run_rename(&mut self, path: &Path, value: &str) {
        if let Err(e) = cfs::validate_name(value) {
            return self.error(e);
        }
        match cfs::rename_in_place(path, value) {
            Ok(new) => {
                self.reload_all();
                self.active_panel().tab_mut().select_path(&new);
            }
            Err(e) => self.error(format!("{}: {e}", path.display())),
        }
    }

    fn run_copy_move(
        &mut self,
        kind: OpKind,
        sources: Vec<PathBuf>,
        value: &str,
        ctx: &egui::Context,
    ) {
        let target = PathBuf::from(value);
        let req = if target.is_dir() || value.ends_with(['/', '\\']) || sources.len() > 1 {
            OpRequest {
                kind,
                sources,
                dest_dir: Some(target),
                rename_to: None,
            }
        } else {
            // One source and a path that is not a folder: copy / move under that name.
            let parent = target.parent().map(Path::to_path_buf).unwrap_or_default();
            let name = target.file_name().map(|n| n.to_string_lossy().into_owned());
            OpRequest {
                kind,
                sources,
                dest_dir: Some(parent),
                rename_to: name,
            }
        };
        self.start_file_op(req, ctx);
    }

    fn run_combine(&mut self, part: PathBuf, value: String, ctx: &egui::Context) {
        let dest = PathBuf::from(value);
        self.jobs.start(tr("cmd.combine").into(), ctx, move |r| {
            std::fs::create_dir_all(&dest).map_err(|e| e.to_string())?;
            let rep = splitter::combine_files(&part, &dest, &mut |done, total| {
                r.report(fraction(done, total), cfs::human_size(done))
            })
            .map_err(|e| e.to_string())?;
            let crc = match rep.crc_ok {
                Some(true) => "CRC OK",
                Some(false) => {
                    return Err(format!(
                        "{}: CRC {}",
                        rep.output.display(),
                        t("mismatch", "incorrect")
                    ));
                }
                None => t("no CRC file", "pas de fichier CRC"),
            };
            Ok(format!(
                "{} {} → {} ({crc})",
                rep.parts,
                t("parts", "parties"),
                rep.output.display()
            ))
        });
    }

    fn run_catalog(&mut self, root: PathBuf, value: String, ctx: &egui::Context) {
        let out = PathBuf::from(value);
        self.jobs.start(tr("cmd.catalog").into(), ctx, move |r| {
            r.report(0.5, root.display().to_string());
            let n =
                catalog::write_catalog(&root, &out, r.cancel_flag()).map_err(|e| e.to_string())?;
            Ok(format!("{n} {} → {}", tr("files"), out.display()))
        });
    }

    fn run_time(&mut self, f: TimeForm) {
        let results = match f.mode {
            TimeMode::Shift => match timestamps::parse_offset(&f.value) {
                Ok(off) => timestamps::shift_times(&f.files, off, f.modified, f.accessed),
                Err(e) => return self.error(e),
            },
            TimeMode::Sequence => {
                let (start, step) = match (parse_date(&f.value), timestamps::parse_offset(&f.step))
                {
                    (Ok(s), Ok(st)) => (s, st),
                    (Err(e), _) | (_, Err(e)) => return self.error(e),
                };
                timestamps::redate(&f.files, start, step)
            }
            TimeMode::Fixed => match parse_date(&f.value) {
                Ok(when) => timestamps::set_times(
                    &f.files,
                    f.modified.then_some(when),
                    f.accessed.then_some(when),
                ),
                Err(e) => return self.error(e),
            },
        };
        let errors: Vec<String> = results.into_iter().filter_map(|(_, r)| r.err()).collect();
        self.reload_all();
        if errors.is_empty() {
            self.status = format!("{} {}", f.files.len(), tr("files"));
        } else {
            self.error(errors.join("\n"));
        }
    }

    fn run_crypt(&mut self, f: CryptForm, ctx: &egui::Context) {
        let dest_dir = PathBuf::from(f.dest.trim());
        let CryptForm {
            files,
            encrypt,
            password,
            ..
        } = f;
        let title = if encrypt {
            tr("cmd.encrypt")
        } else {
            tr("cmd.decrypt")
        };
        self.jobs.start(title.into(), ctx, move |r| {
            let total = files.len();
            let mut errors = Vec::new();
            for (i, src) in files.iter().enumerate() {
                let default = if encrypt {
                    crypt::default_encrypted_path(src)
                } else {
                    crypt::default_decrypted_path(src)
                };
                let dest = dest_dir.join(default.file_name().unwrap_or_default());
                if dest.exists() {
                    errors.push(format!(
                        "{}: {}",
                        dest.display(),
                        t("already exists", "existe déjà")
                    ));
                    continue;
                }
                let frac = i as f32 / total.max(1) as f32;
                let name = src.display().to_string();
                let mut progress = |_done: u64| r.report(frac, name.clone());
                let res = if encrypt {
                    crypt::encrypt_file(src, &dest, &password, &mut progress)
                } else {
                    crypt::decrypt_file(src, &dest, &password, &mut progress)
                };
                match res {
                    Ok(()) => {}
                    Err(e) if e.kind() == std::io::ErrorKind::Interrupted => break,
                    Err(e) => errors.push(format!("{}: {e}", src.display())),
                }
            }
            if errors.is_empty() {
                Ok(format!("{total} {}", tr("files")))
            } else {
                Err(errors.join("\n"))
            }
        });
    }
}
