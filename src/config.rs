//! Persistent settings (JSON).
//!
//! The settings folder is resolved by [`crate::core::settings_dir`]: by default the `config`
//! subfolder of the installation folder, or the folder the user chose (Windows setup, first
//! launch or Settings); portable mode and the platform folder still work.

use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use serde::{Deserialize, Serialize};

use crate::core::fs::SortOrder;
use crate::core::ftp::FtpSite;
use crate::core::settings_dir::{self, Location, Places, SETTINGS_FILE};
use crate::i18n::Lang;

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct HotlistItem {
    pub name: String,
    pub path: PathBuf,
}

/// A user button / command ("open selected with" presets, like lst2multi).
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct UserCommand {
    pub name: String,
    /// Program and arguments; `%s` is replaced by the selected files.
    pub command: String,
    pub once_per_file: bool,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Columns {
    pub ext: bool,
    pub size: bool,
    pub date: bool,
    pub attrs: bool,
}

impl Default for Columns {
    fn default() -> Self {
        Columns {
            ext: true,
            size: true,
            date: true,
            attrs: true,
        }
    }
}

impl Columns {
    /// Default widths in points: Name (its minimum), Ext, Size, Date, Attr.
    pub const NAME_MIN: f32 = 80.0;
    pub const EXT: f32 = 50.0;
    pub const SIZE: f32 = 80.0;
    pub const DATE: f32 = 130.0;
    pub const ATTRS: f32 = 90.0;

    /// The shown columns that fit in `width` beside the Name column, `gap` apart. When the
    /// panel is too narrow, the least useful go first: Attr, then Ext, then Date, then Size.
    pub fn fitting(&self, width: f32, gap: f32) -> Columns {
        let mut out = self.clone();
        let used = |c: &Columns| {
            let extra = [
                (c.ext, Self::EXT),
                (c.size, Self::SIZE),
                (c.date, Self::DATE),
                (c.attrs, Self::ATTRS),
            ];
            extra
                .iter()
                .filter(|(on, _)| *on)
                .map(|(_, w)| w + gap)
                .sum::<f32>()
                + Self::NAME_MIN
        };
        for drop in [
            |c: &mut Columns| c.attrs = false,
            |c: &mut Columns| c.ext = false,
            |c: &mut Columns| c.date = false,
            |c: &mut Columns| c.size = false,
        ] {
            if used(&out) <= width {
                break;
            }
            drop(&mut out);
        }
        out
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, Default)]
pub struct PanelConfig {
    pub tabs: Vec<PathBuf>,
    pub active_tab: usize,
    pub sort: SortOrder,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(default)]
pub struct Settings {
    pub lang: Lang,
    pub dark_mode: bool,
    pub font_size: f32,
    pub show_hidden: bool,
    pub use_trash: bool,
    pub confirm_delete: bool,
    pub log_operations: bool,
    /// External editor command; empty = the built-in editor.
    pub editor: String,
    /// Terminal command run in the current folder; empty = platform default.
    pub terminal: String,
    pub columns: Columns,
    pub left: PanelConfig,
    pub right: PanelConfig,
    pub hotlist: Vec<HotlistItem>,
    pub user_commands: Vec<UserCommand>,
    pub ftp_sites: Vec<FtpSite>,
    pub history: Vec<PathBuf>,
    /// Network drive addresses connected before, most recent first (never a password).
    pub net_shares: Vec<String>,
    /// Look for a newer release at start-up (one request to GitHub).
    pub check_updates: bool,
}

impl Default for Settings {
    fn default() -> Self {
        Settings {
            lang: Lang::from_env(),
            dark_mode: true,
            font_size: 14.0,
            show_hidden: false,
            use_trash: true,
            confirm_delete: true,
            log_operations: false,
            check_updates: true,
            editor: String::new(),
            terminal: String::new(),
            columns: Columns::default(),
            left: PanelConfig::default(),
            right: PanelConfig::default(),
            hotlist: Vec::new(),
            user_commands: Vec::new(),
            ftp_sites: Vec::new(),
            history: Vec::new(),
            net_shares: Vec::new(),
        }
    }
}

impl Settings {
    /// Loads the settings, falling back to defaults when missing or unreadable.
    pub fn load(path: &Path) -> Settings {
        fs::read_to_string(path)
            .ok()
            .and_then(|s| serde_json::from_str(&s).ok())
            .unwrap_or_default()
    }

    /// Writes atomically (temporary file + rename).
    pub fn save(&self, path: &Path) -> io::Result<()> {
        if let Some(dir) = path.parent() {
            fs::create_dir_all(dir)?;
        }
        let json = serde_json::to_string_pretty(self).map_err(io::Error::other)?;
        let tmp = path.with_extension("json.tmp");
        fs::write(&tmp, json)?;
        fs::rename(&tmp, path)
    }

    /// Remembers a visited folder (most recent first, 30 max, no duplicates).
    pub fn push_history(&mut self, dir: &Path) {
        self.history.retain(|p| p != dir);
        self.history.insert(0, dir.to_path_buf());
        self.history.truncate(30);
    }
}

/// The resolved settings folder, cached: [`use_dir`] replaces it.
static LOCATION: Mutex<Option<Location>> = Mutex::new(None);

/// Where the settings live and why (resolved once per process).
pub fn location() -> Location {
    let mut cached = LOCATION.lock().unwrap_or_else(|e| e.into_inner());
    cached
        .get_or_insert_with(|| settings_dir::resolve(&Places::current()))
        .clone()
}

/// The folder holding the settings, the operation log and the password store.
pub fn config_dir() -> PathBuf {
    location().dir
}

/// Makes `dir` the settings folder from now on: moves the settings files there (files it
/// already has win) and records the choice in a pointer file.
pub fn use_dir(dir: &Path) -> io::Result<()> {
    let places = Places::current();
    if places.env.is_some() {
        return Err(io::Error::other("D3SK_CONFIG_DIR is set"));
    }
    let dir = std::path::absolute(dir)?;
    let old = location();
    settings_dir::move_files(&old.dir, &dir)?;
    // Leaving portable mode: the settings file next to the program is the marker. It
    // stayed only when the target already had settings; set it aside so it stops winning.
    let marker = old.dir.join(SETTINGS_FILE);
    if old.source == settings_dir::Source::Portable && marker.is_file() && old.dir != dir {
        fs::rename(&marker, old.dir.join(format!("{SETTINGS_FILE}.old")))?;
    }
    settings_dir::write_pointer(places.pointer_home(), &dir)?;
    let now = settings_dir::resolve(&places);
    let moved = now.dir == dir;
    *LOCATION.lock().unwrap_or_else(|e| e.into_inner()) = Some(now);
    if moved {
        Ok(())
    } else {
        Err(io::Error::other(
            "another settings location takes precedence",
        ))
    }
}

pub fn settings_path() -> PathBuf {
    config_dir().join(SETTINGS_FILE)
}

/// The encrypted password store (FTP and archive passwords).
pub fn passwords_path() -> PathBuf {
    config_dir().join("passwords.age")
}

/// The operation log written when `log_operations` is on.
pub fn log_path() -> PathBuf {
    config_dir().join("operations.log")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn columns_drop_attr_ext_date_size_in_that_order_when_narrow() {
        let all = Columns::default();
        assert_eq!(all.fitting(2000.0, 0.0), all);
        let wide = 80.0 + 50.0 + 80.0 + 130.0 + 90.0;
        assert_eq!(all.fitting(wide, 0.0), all);
        let no_attr = all.fitting(wide - 1.0, 0.0);
        assert!(!no_attr.attrs && no_attr.ext && no_attr.size && no_attr.date);
        let size_date = all.fitting(80.0 + 80.0 + 130.0, 0.0);
        assert!(!size_date.ext && size_date.size && size_date.date);
        let size_only = all.fitting(200.0, 0.0);
        assert!(size_only.size && !size_only.date);
        assert_eq!(
            all.fitting(10.0, 0.0),
            Columns {
                ext: false,
                size: false,
                date: false,
                attrs: false
            }
        );
        // Columns the user turned off stay off.
        let some = Columns {
            ext: false,
            ..Columns::default()
        };
        assert!(!some.fitting(2000.0, 0.0).ext);
    }

    #[test]
    fn round_trip_and_defaults_for_missing_fields() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("s.json");
        let mut s = Settings {
            show_hidden: true,
            ..Settings::default()
        };
        s.push_history(Path::new("/a"));
        s.push_history(Path::new("/b"));
        s.push_history(Path::new("/a"));
        assert_eq!(s.history, [PathBuf::from("/a"), PathBuf::from("/b")]);
        s.save(&path).unwrap();
        let json = |s: &Settings| serde_json::to_string(s).unwrap();
        assert_eq!(json(&Settings::load(&path)), json(&s));

        fs::write(&path, r#"{"show_hidden": true}"#).unwrap();
        let partial = Settings::load(&path);
        assert!(partial.show_hidden);
        assert!(partial.use_trash);
        assert_eq!(
            Settings::load(&dir.path().join("missing.json")).font_size,
            14.0
        );
    }
}
