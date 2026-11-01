//! Network drives: connect from the form, disconnect the active panel's drive. The OS does
//! the mount (`core::netdrive`), on a background job since it can take a while.

use eframe::egui;

use crate::core::fs as cfs;
use crate::core::netdrive::{self, Credentials};
use crate::i18n::{t, tr};
use crate::ui::dialogs::{Dialog, NetForm};
use crate::ui::{App, AppEvent};

/// How many addresses the form remembers.
const RECENT_SHARES: usize = 10;

impl App {
    pub(crate) fn net_connect_dialog(&mut self) {
        let form = NetForm::with_recent(&self.shared.settings.net_shares);
        self.dialog = Some(Dialog::NetConnect(form));
    }

    pub(crate) fn run_net_connect(&mut self, f: NetForm, ctx: &egui::Context) {
        let share = match netdrive::parse(&f.address) {
            Ok(s) => s,
            Err(e) => return self.error(e),
        };
        let letter = match netdrive::drive_letter(&f.letter) {
            Ok(l) => l,
            Err(e) => return self.error(e),
        };
        let address = f.address.trim().to_string();
        let recent = &mut self.shared.settings.net_shares;
        recent.retain(|a| a != &address);
        recent.insert(0, address);
        recent.truncate(RECENT_SHARES);
        let creds = Credentials {
            user: f.user.trim().to_string(),
            domain: f.domain.trim().to_string(),
            password: f.password,
        };
        let tx = self.shared.tx.clone();
        self.jobs
            .start(tr("cmd.net_connect").into(), ctx, move |r| {
                r.report(0.0, share.uri());
                let dir = netdrive::connect(&share, &creds, letter.as_deref())?;
                let _ = tx.send(AppEvent::OpenDir(dir.clone()));
                Ok(format!(
                    "{} {} → {}",
                    share.uri(),
                    t("connected", "connecté"),
                    dir.display()
                ))
            });
    }

    pub(crate) fn net_disconnect(&mut self, ctx: &egui::Context) {
        let Some(dir) = self.active_dir() else {
            return self.error(t(
                "Open a folder of the network drive first.",
                "Ouvrez d'abord un dossier du lecteur réseau.",
            ));
        };
        let tx = self.shared.tx.clone();
        self.jobs
            .start(tr("cmd.net_disconnect").into(), ctx, move |_| {
                let what = netdrive::disconnect(&dir)?;
                // The panel's folder is gone: leave it.
                let _ = tx.send(AppEvent::OpenDir(cfs::home_dir()));
                Ok(format!("{what} {}", t("disconnected", "déconnecté")))
            });
    }
}
