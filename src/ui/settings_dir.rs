//! Choosing the settings folder: asked once on a first launch, and from
//! `Command::SettingsFolder` (Configuration menu, Settings window) at any time.

use std::path::Path;

use super::App;
use super::dialogs::{Dialog, InputAction};
use crate::config::{self, Settings};
use crate::core::settings_dir::{Places, Source};
use crate::i18n::t;

impl App {
    /// Opens the folder question; `first_launch` proposes the default folder.
    pub(crate) fn ask_settings_dir(&mut self, first_launch: bool) {
        let location = config::location();
        if location.source == Source::Env {
            let text = t(
                "D3SK_CONFIG_DIR is set: it decides the settings folder.",
                "D3SK_CONFIG_DIR est défini : il décide du dossier des réglages.",
            );
            self.dialog = Some(Dialog::message(
                t("Settings folder", "Dossier des réglages"),
                text,
            ));
            return;
        }
        let (dir, label) = if first_launch {
            (
                Places::current().default_dir(),
                t(
                    "Where should d3skcommander keep your preferences? You can change it later in Configuration > Settings folder.",
                    "Où d3skcommander doit-il garder vos préférences ? Vous pourrez le changer dans Configuration > Dossier des réglages.",
                ),
            )
        } else {
            (
                location.dir,
                t(
                    "Your preferences, passwords and log move to this folder (settings already there are kept).",
                    "Vos préférences, mots de passe et journal vont dans ce dossier (des réglages déjà présents sont gardés).",
                ),
            )
        };
        self.dialog = Some(Dialog::input(
            t("Settings folder", "Dossier des réglages"),
            label,
            dir.to_string_lossy(),
            InputAction::SettingsDir { first_launch },
        ));
    }

    /// Makes `value` the settings folder and loads the settings found there.
    pub(crate) fn run_settings_dir(&mut self, value: &str, first_launch: bool) {
        if value.is_empty() {
            return;
        }
        if !first_launch {
            self.save_settings();
        }
        if let Err(e) = config::use_dir(Path::new(value)) {
            self.error(format!("{value}: {e}"));
            return;
        }
        self.settings_path = config::settings_path();
        if self.settings_path.is_file() {
            self.shared.settings = Settings::load(&self.settings_path);
        }
        self.status = format!(
            "{}: {}",
            t("Settings folder", "Dossier des réglages"),
            config::config_dir().display()
        );
        self.shared.send(super::AppEvent::SettingsChanged);
    }
}
