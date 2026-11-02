//! Where the settings folder is (preferences, password store, operation log).
//!
//! In order, the first that applies wins:
//! 1. `D3SK_CONFIG_DIR` (an override for tests and one-off runs);
//! 2. portable mode: `d3skcommander.json` next to the program;
//! 3. a [`POINTER`] file in the installation folder, else in the platform folder: one line,
//!    the chosen folder (relative = relative to the pointer's folder). The Windows setup
//!    writes it; the first-launch question and Settings write it too;
//! 4. the platform folder (`~/.config/d3skcommander`, `%APPDATA%\…`, `~/Library/…`) when it
//!    already holds settings (installs from before the choice existed);
//! 5. the default: `<installation folder>/config` when that folder is writable, else the
//!    platform folder.
//!
//! The installation folder is where the user put the program: the folder of the Windows
//! setup's install, of a portable program or of an AppImage. A `.deb` (system folders), a
//! macOS `.app` (the bundle is replaced on update) and a source build have none.

use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use super::update::InstallKind;

/// The settings file; next to the program it also turns portable mode on.
pub const SETTINGS_FILE: &str = "d3skcommander.json";
/// The file naming the chosen settings folder.
pub const POINTER: &str = "config-location.txt";
/// The default settings folder inside the installation folder.
pub const SUBFOLDER: &str = "config";
/// Every file kept in the settings folder (moved together).
pub const FILES: [&str; 3] = [SETTINGS_FILE, "passwords.age", "operations.log"];

/// What [`resolve`] looks at, gathered by [`Places::current`].
#[derive(Clone, Debug, Default)]
pub struct Places {
    /// `D3SK_CONFIG_DIR`, when set and not empty.
    pub env: Option<PathBuf>,
    /// The folder of the running program.
    pub exe_dir: Option<PathBuf>,
    /// The installation folder (see the module doc).
    pub install_dir: Option<PathBuf>,
    /// The program may write in `install_dir`.
    pub install_writable: bool,
    /// The platform's per-user config folder for the app.
    pub platform_dir: PathBuf,
}

/// Why the settings live where they do.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Source {
    Env,
    Portable,
    /// Chosen by the user (a pointer file).
    Chosen,
    /// The platform folder already held settings.
    Existing,
    /// Nobody chose yet.
    Default,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Location {
    pub dir: PathBuf,
    pub source: Source,
}

impl Location {
    /// True on a first launch: nobody chose a folder and no settings exist yet.
    pub fn needs_choice(&self) -> bool {
        self.source == Source::Default && !self.dir.join(SETTINGS_FILE).exists()
    }
}

impl Places {
    pub fn current() -> Places {
        let exe = std::env::current_exe().ok();
        let install_dir = install_dir(&super::update::detect(&super::update::Facts::current()));
        Places {
            env: std::env::var_os("D3SK_CONFIG_DIR")
                .filter(|v| !v.is_empty())
                .map(PathBuf::from),
            exe_dir: exe.and_then(|e| e.parent().map(Path::to_path_buf)),
            install_writable: install_dir.as_deref().is_some_and(writable),
            install_dir,
            platform_dir: directories::ProjectDirs::from("", "", "d3skcommander")
                .map(|d| d.config_dir().to_path_buf())
                .unwrap_or_else(|| PathBuf::from(".")),
        }
    }

    /// Where a new pointer file goes: the installation folder when writable, else the
    /// platform folder.
    pub fn pointer_home(&self) -> &Path {
        match &self.install_dir {
            Some(d) if self.install_writable => d,
            _ => &self.platform_dir,
        }
    }

    /// The folder proposed on a first launch.
    pub fn default_dir(&self) -> PathBuf {
        match &self.install_dir {
            Some(d) if self.install_writable => d.join(SUBFOLDER),
            _ => self.platform_dir.clone(),
        }
    }
}

/// The installation folder of an install kind (see the module doc).
pub fn install_dir(kind: &InstallKind) -> Option<PathBuf> {
    match kind {
        InstallKind::AppImage(p) | InstallKind::WindowsSetup(p) | InstallKind::Portable(p) => {
            p.parent().map(Path::to_path_buf)
        }
        InstallKind::Deb(_) | InstallKind::MacApp(_) | InstallKind::Source => None,
    }
}

pub fn resolve(p: &Places) -> Location {
    let at = |dir: PathBuf, source| Location { dir, source };
    if let Some(dir) = &p.env {
        return at(dir.clone(), Source::Env);
    }
    if let Some(dir) = p
        .exe_dir
        .as_ref()
        .filter(|d| d.join(SETTINGS_FILE).is_file())
    {
        return at(dir.clone(), Source::Portable);
    }
    let homes = p.install_dir.iter().chain([&p.platform_dir]);
    if let Some(dir) = homes.filter_map(|h| read_pointer(h)).next() {
        return at(dir, Source::Chosen);
    }
    if p.platform_dir.join(SETTINGS_FILE).is_file() {
        return at(p.platform_dir.clone(), Source::Existing);
    }
    at(p.default_dir(), Source::Default)
}

/// The folder named by `<home>/POINTER`, if that file exists and names one. The file is
/// UTF-8 (BOM allowed) or UTF-16LE with a BOM (what the Windows setup writes).
pub fn read_pointer(home: &Path) -> Option<PathBuf> {
    let bytes = fs::read(home.join(POINTER)).ok()?;
    let text = match bytes.as_slice() {
        [0xFF, 0xFE, rest @ ..] => {
            let units: Vec<u16> = rest
                .as_chunks::<2>()
                .0
                .iter()
                .map(|c| u16::from_le_bytes(*c))
                .collect();
            String::from_utf16_lossy(&units)
        }
        [0xEF, 0xBB, 0xBF, rest @ ..] => String::from_utf8_lossy(rest).into_owned(),
        _ => String::from_utf8_lossy(&bytes).into_owned(),
    };
    let line = text.lines().map(str::trim).find(|l| !l.is_empty())?;
    Some(home.join(line))
}

/// Writes `<home>/POINTER` naming `dir` (relative when `dir` lies inside `home`, so a
/// portable folder can move).
pub fn write_pointer(home: &Path, dir: &Path) -> io::Result<()> {
    fs::create_dir_all(home)?;
    let named = dir.strip_prefix(home).unwrap_or(dir);
    let named = if named.as_os_str().is_empty() {
        Path::new(".")
    } else {
        named
    };
    let tmp = home.join(format!("{POINTER}.tmp"));
    fs::write(&tmp, format!("{}\n", named.display()))?;
    fs::rename(&tmp, home.join(POINTER))
}

/// Moves the settings files from `from` to `to`. A file `to` already has is kept there
/// and left in `from` too (the settings found at the target win).
pub fn move_files(from: &Path, to: &Path) -> io::Result<()> {
    fs::create_dir_all(to)?;
    if same_dir(from, to) {
        return Ok(());
    }
    for name in FILES {
        let (src, dst) = (from.join(name), to.join(name));
        if !src.is_file() || dst.exists() {
            continue;
        }
        if fs::rename(&src, &dst).is_err() {
            // Another drive: copy, then remove the original.
            fs::copy(&src, &dst)?;
            fs::remove_file(&src)?;
        }
    }
    Ok(())
}

fn same_dir(a: &Path, b: &Path) -> bool {
    match (a.canonicalize(), b.canonicalize()) {
        (Ok(a), Ok(b)) => a == b,
        _ => a == b,
    }
}

/// True when a file can be created in `dir` (tried, then removed).
fn writable(dir: &Path) -> bool {
    let probe = dir.join(".d3sk-write-test");
    let ok = fs::write(&probe, b"").is_ok();
    let _ = fs::remove_file(&probe);
    ok
}

#[cfg(test)]
mod tests {
    use super::*;

    fn places(root: &Path) -> Places {
        let install = root.join("install");
        fs::create_dir_all(&install).unwrap();
        Places {
            env: None,
            exe_dir: Some(install.clone()),
            install_writable: true,
            install_dir: Some(install),
            platform_dir: root.join("platform"),
        }
    }

    #[test]
    fn a_first_launch_proposes_the_config_subfolder_of_the_install() {
        let t = tempfile::tempdir().unwrap();
        let p = places(t.path());
        let l = resolve(&p);
        assert_eq!(l.dir, t.path().join("install").join("config"));
        assert_eq!(l.source, Source::Default);
        assert!(l.needs_choice());
        fs::create_dir_all(&l.dir).unwrap();
        fs::write(l.dir.join(SETTINGS_FILE), "{}").unwrap();
        assert!(!resolve(&p).needs_choice());
    }

    #[test]
    fn without_a_writable_install_folder_the_platform_folder_is_used() {
        let t = tempfile::tempdir().unwrap();
        let mut p = places(t.path());
        p.install_writable = false;
        assert_eq!(resolve(&p).dir, p.platform_dir);
        assert_eq!(p.pointer_home(), p.platform_dir);
        p.install_dir = None;
        assert_eq!(resolve(&p).dir, p.platform_dir);
    }

    #[test]
    fn existing_platform_settings_win_over_the_default() {
        let t = tempfile::tempdir().unwrap();
        let p = places(t.path());
        fs::create_dir_all(&p.platform_dir).unwrap();
        fs::write(p.platform_dir.join(SETTINGS_FILE), "{}").unwrap();
        assert_eq!(
            resolve(&p),
            Location {
                dir: p.platform_dir.clone(),
                source: Source::Existing
            }
        );
    }

    #[test]
    fn a_pointer_wins_over_existing_settings_and_the_portable_marker_over_both() {
        let t = tempfile::tempdir().unwrap();
        let mut p = places(t.path());
        fs::create_dir_all(&p.platform_dir).unwrap();
        fs::write(p.platform_dir.join(SETTINGS_FILE), "{}").unwrap();
        let chosen = t.path().join("elsewhere");
        write_pointer(p.pointer_home(), &chosen).unwrap();
        assert_eq!(resolve(&p).dir, chosen);
        assert_eq!(resolve(&p).source, Source::Chosen);

        let install = p.install_dir.clone().unwrap();
        fs::write(install.join(SETTINGS_FILE), "{}").unwrap();
        assert_eq!(resolve(&p).source, Source::Portable);

        p.env = Some(t.path().join("env"));
        assert_eq!(resolve(&p).dir, t.path().join("env"));
    }

    #[test]
    fn a_pointer_in_the_platform_folder_is_found_without_an_install_folder() {
        let t = tempfile::tempdir().unwrap();
        let mut p = places(t.path());
        p.install_dir = None;
        let chosen = t.path().join("synced");
        write_pointer(p.pointer_home(), &chosen).unwrap();
        assert_eq!(resolve(&p).dir, chosen);
    }

    #[test]
    fn pointers_inside_their_folder_are_relative_and_utf16_is_read() {
        let t = tempfile::tempdir().unwrap();
        let home = t.path();
        write_pointer(home, &home.join("config")).unwrap();
        assert_eq!(fs::read_to_string(home.join(POINTER)).unwrap(), "config\n");
        assert_eq!(read_pointer(home), Some(home.join("config")));

        // The Windows setup: UTF-16LE with a BOM, CRLF, a non-ASCII path.
        let mut bytes = vec![0xFF, 0xFE];
        for u in "D:\\Réglages\r\n".encode_utf16() {
            bytes.extend(u.to_le_bytes());
        }
        fs::write(home.join(POINTER), bytes).unwrap();
        assert_eq!(read_pointer(home), Some(home.join("D:\\Réglages")));

        fs::write(home.join(POINTER), "\u{feff}  \n").unwrap();
        assert_eq!(read_pointer(home), None);
    }

    #[test]
    fn moving_keeps_what_the_target_already_has() {
        let t = tempfile::tempdir().unwrap();
        let (from, to) = (t.path().join("a"), t.path().join("b"));
        fs::create_dir_all(&from).unwrap();
        fs::create_dir_all(&to).unwrap();
        fs::write(from.join(SETTINGS_FILE), "old").unwrap();
        fs::write(from.join("passwords.age"), "pw").unwrap();
        fs::write(to.join(SETTINGS_FILE), "target").unwrap();
        move_files(&from, &to).unwrap();
        assert_eq!(
            fs::read_to_string(to.join(SETTINGS_FILE)).unwrap(),
            "target"
        );
        assert_eq!(fs::read_to_string(to.join("passwords.age")).unwrap(), "pw");
        assert!(!from.join("passwords.age").exists());
        assert!(from.join(SETTINGS_FILE).exists());
        // Same folder: nothing moves, nothing breaks.
        move_files(&to, &to).unwrap();
        assert!(to.join("passwords.age").exists());
    }

    #[test]
    fn install_folders_per_kind() {
        let exe = PathBuf::from("/x/app/d3skcommander");
        assert_eq!(
            install_dir(&InstallKind::Portable(exe.clone())),
            Some(PathBuf::from("/x/app"))
        );
        assert_eq!(
            install_dir(&InstallKind::AppImage("/x/d3sk.AppImage".into())),
            Some(PathBuf::from("/x"))
        );
        assert_eq!(install_dir(&InstallKind::Deb(exe.clone())), None);
        assert_eq!(install_dir(&InstallKind::MacApp("/A/d.app".into())), None);
        assert_eq!(install_dir(&InstallKind::Source), None);
    }
}
