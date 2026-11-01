//! The "connect a network drive" form.

use eframe::egui;

use super::{Dialog, Outcome, decide, ok_cancel};
use crate::core::netdrive;
use crate::i18n::{t, tr};

#[derive(Default)]
pub struct NetForm {
    pub address: String,
    pub user: String,
    pub domain: String,
    pub password: String,
    /// Windows only: the drive letter to map (`Z:`), empty = none.
    pub letter: String,
    pub focused: bool,
}

impl NetForm {
    /// An empty form on the last address used.
    pub fn with_recent(recent: &[String]) -> NetForm {
        NetForm {
            address: recent.first().cloned().unwrap_or_default(),
            ..NetForm::default()
        }
    }

    /// What is wrong with the form, if anything (shown under the fields).
    pub fn problem(&self) -> Option<String> {
        if self.address.trim().is_empty() {
            return None;
        }
        netdrive::parse(&self.address)
            .err()
            .or_else(|| netdrive::drive_letter(&self.letter).err())
    }

    pub fn valid(&self) -> bool {
        !self.address.trim().is_empty() && self.problem().is_none()
    }
}

fn row(ui: &mut egui::Ui, label: &str, add: impl FnOnce(&mut egui::Ui) -> egui::Response) {
    ui.horizontal(|ui| {
        ui.add_sized([110.0, 20.0], egui::Label::new(label));
        add(ui);
    });
}

pub(super) fn net_ui(ui: &mut egui::Ui, mut f: NetForm, recent: &[String], enter: bool) -> Outcome {
    ui.heading(tr("cmd.net_connect"));
    let hint = if cfg!(windows) {
        r"\\server\share"
    } else {
        r"\\server\share, smb://server/share, sftp://host"
    };
    let width = ui.available_width().min(420.0);
    row(ui, t("Address", "Adresse"), |ui| {
        let r = ui.add(
            egui::TextEdit::singleline(&mut f.address)
                .hint_text(hint)
                .desired_width(width - 150.0),
        );
        if !f.focused {
            r.request_focus();
        }
        r
    });
    f.focused = true;
    if !recent.is_empty() {
        row(ui, t("Recent", "Récents"), |ui| {
            egui::ComboBox::from_id_salt("net-recent")
                .selected_text("…")
                .width(width - 150.0)
                .show_ui(ui, |ui| {
                    for a in recent {
                        if ui.selectable_label(false, a).clicked() {
                            f.address = a.clone();
                        }
                    }
                })
                .response
        });
    }
    row(ui, t("User", "Utilisateur"), |ui| {
        ui.add(
            egui::TextEdit::singleline(&mut f.user)
                .hint_text(t("(current login)", "(session en cours)"))
                .desired_width(width - 150.0),
        )
    });
    row(ui, t("Domain", "Domaine"), |ui| {
        ui.add(egui::TextEdit::singleline(&mut f.domain).desired_width(width - 150.0))
    });
    row(ui, tr("password"), |ui| {
        ui.add(
            egui::TextEdit::singleline(&mut f.password)
                .password(true)
                .desired_width(width - 150.0),
        )
    });
    if cfg!(windows) {
        row(ui, t("Drive letter", "Lettre de lecteur"), |ui| {
            ui.add(
                egui::TextEdit::singleline(&mut f.letter)
                    .hint_text("Z:")
                    .desired_width(40.0),
            )
        });
    }
    if let Some(problem) = f.problem() {
        ui.colored_label(ui.visuals().error_fg_color, problem);
    }
    ui.weak(t(
        "Mounted by the system, so other programs see it too. The password is not saved.",
        "Monté par le système, visible aussi des autres programmes. Le mot de passe n'est pas enregistré.",
    ));
    let valid = f.valid();
    let (ok, cancel) = ok_cancel(ui, valid);
    decide((ok || enter) && valid, cancel, Dialog::NetConnect(f))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_form_explains_a_bad_address_or_drive_letter() {
        let mut f = NetForm::with_recent(&[]);
        assert!(
            !f.valid() && f.problem().is_none(),
            "empty: not valid, no error yet"
        );
        f.address = r"\\nas".into();
        assert!(f.problem().unwrap().contains("share"));
        f.address = r"\\nas\media".into();
        assert!(f.valid());
        f.letter = "ZZ".into();
        assert!(!f.valid());
        let f = NetForm::with_recent(&["//a/b".into(), "//c/d".into()]);
        assert_eq!(f.address, "//a/b");
    }
}
