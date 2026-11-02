//! Self-update, the actions: ask GitHub for the latest release, download a release file
//! (checked against the SHA-256 GitHub computed at upload) and put it in place for each
//! [`InstallKind`]. Every step blocks: run it on a background thread.

use std::fs;
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::process::Command;

use sha2::{Digest, Sha256};

use super::{Asset, InstallKind, Release, parse_release};
use crate::core::archive;

fn agent() -> ureq::Agent {
    ureq::Agent::config_builder()
        .timeout_global(Some(std::time::Duration::from_secs(600)))
        .build()
        .into()
}

const USER_AGENT: &str = concat!("d3skcommander/", env!("CARGO_PKG_VERSION"));

/// The latest final release at `api_url` (see [`super::latest_release_url`]).
pub fn fetch_latest(api_url: &str) -> Result<Release, String> {
    let mut resp = agent()
        .get(api_url)
        .header("User-Agent", USER_AGENT)
        .header("Accept", "application/vnd.github+json")
        .header("X-GitHub-Api-Version", "2022-11-28")
        .call()
        .map_err(|e| match e {
            ureq::Error::StatusCode(404) => format!(
                "no published release at {api_url} (none yet, or the repository is private)"
            ),
            e => e.to_string(),
        })?;
    let body = resp
        .body_mut()
        .read_to_string()
        .map_err(|e| e.to_string())?;
    parse_release(&body)
}

/// Downloads `asset` into `dir`, calling `progress(done, total)` (false = cancel), and
/// checks its SHA-256. A file without a published checksum is refused.
pub fn download(
    asset: &Asset,
    dir: &Path,
    progress: &mut dyn FnMut(u64, u64) -> bool,
) -> Result<PathBuf, String> {
    let expected = asset
        .sha256
        .clone()
        .ok_or_else(|| format!("{}: no checksum published, not installed", asset.name))?;
    fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let dest = dir.join(&asset.name);
    let resp = agent()
        .get(&asset.url)
        .header("User-Agent", USER_AGENT)
        .call()
        .map_err(|e| e.to_string())?;
    let mut reader = resp.into_body().into_reader();
    let mut out = fs::File::create(&dest).map_err(|e| e.to_string())?;
    let mut hasher = Sha256::new();
    let mut buf = vec![0u8; 64 * 1024];
    let mut done = 0u64;
    loop {
        let n = reader.read(&mut buf).map_err(|e| e.to_string())?;
        if n == 0 {
            break;
        }
        out.write_all(&buf[..n]).map_err(|e| e.to_string())?;
        hasher.update(&buf[..n]);
        done += n as u64;
        if !progress(done, asset.size) {
            drop(out);
            let _ = fs::remove_file(&dest);
            return Err("cancelled".into());
        }
    }
    out.flush().map_err(|e| e.to_string())?;
    let got = hex(&hasher.finalize());
    if got != expected {
        let _ = fs::remove_file(&dest);
        return Err(format!(
            "{}: checksum mismatch (expected {expected}, got {got})",
            asset.name
        ));
    }
    Ok(dest)
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

/// The program to start once the app has closed (the updated app, or the Windows setup
/// that installs it and starts it again).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Relaunch {
    pub program: PathBuf,
    pub args: Vec<String>,
}

impl Relaunch {
    fn of(program: impl Into<PathBuf>) -> Relaunch {
        Relaunch {
            program: program.into(),
            args: Vec::new(),
        }
    }

    pub fn spawn(&self) -> io::Result<()> {
        Command::new(&self.program)
            .args(&self.args)
            .spawn()
            .map(drop)
    }
}

/// Puts the downloaded `file` in place for `kind`. Returns what to start afterwards.
pub fn install(kind: &InstallKind, file: &Path) -> Result<Relaunch, String> {
    match kind {
        InstallKind::AppImage(target) => {
            replace_file(file, target).map_err(|e| e.to_string())?;
            Ok(Relaunch::of(target))
        }
        InstallKind::Deb(exe) => {
            install_deb(file)?;
            Ok(Relaunch::of(exe))
        }
        InstallKind::MacApp(bundle) => {
            replace_bundle(file, bundle).map_err(|e| e.to_string())?;
            Ok(Relaunch {
                program: "open".into(),
                args: vec!["-n".into(), bundle.to_string_lossy().into_owned()],
            })
        }
        // /S: silent; the setup closes the running app itself; /R: start it again after.
        InstallKind::WindowsSetup(_) => Ok(Relaunch {
            program: file.to_path_buf(),
            args: vec!["/S".into(), "/R".into()],
        }),
        InstallKind::Portable(exe) => {
            let new = unpack_program(file, exe).map_err(|e| e.to_string())?;
            let done = replace_file(&new, exe);
            let _ = fs::remove_dir_all(update_dir(exe));
            done.map_err(|e| e.to_string())?;
            Ok(Relaunch::of(exe))
        }
        InstallKind::Source => {
            Err("built from source: update with git pull, then cargo build".into())
        }
    }
}

fn install_deb(file: &Path) -> Result<(), String> {
    let manual = format!("sudo dpkg -i \"{}\"", file.display());
    let status = Command::new("pkexec")
        .arg("/usr/bin/dpkg")
        .arg("-i")
        .arg(file)
        .status()
        .map_err(|e| format!("pkexec: {e} — install it by hand: {manual}"))?;
    match status.code() {
        Some(0) => Ok(()),
        Some(126) | Some(127) => Err(format!("administrator rights refused — or run: {manual}")),
        _ => Err(format!("dpkg failed ({status}) — try: {manual}")),
    }
}

/// Replaces `target` by a copy of `new`, executable. The copy is written beside the
/// target first, so the final step is a rename on one file system. A running program
/// can be renamed but not overwritten on Windows: the old one moves to `<name>.old`,
/// removed by [`remove_leftovers`] at the next start.
pub fn replace_file(new: &Path, target: &Path) -> io::Result<()> {
    let staged = sibling(target, ".update-new");
    fs::copy(new, &staged)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(&staged, fs::Permissions::from_mode(0o755))?;
    }
    if cfg!(windows) && target.exists() {
        let old = sibling(target, ".old");
        let _ = fs::remove_file(&old);
        fs::rename(target, &old)?;
    }
    fs::rename(&staged, target).inspect_err(|_| {
        let _ = fs::remove_file(&staged);
    })
}

/// Removes what an update left beside `exe` (the replaced Windows program).
pub fn remove_leftovers(exe: &Path) {
    let _ = fs::remove_file(sibling(exe, ".old"));
}

fn sibling(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path.file_name().unwrap_or_default().to_os_string();
    name.push(suffix);
    path.with_file_name(name)
}

/// The scratch folder beside `path`: on its file system, so the final rename works.
fn update_dir(path: &Path) -> PathBuf {
    path.parent().unwrap_or(Path::new(".")).join(".d3sk-update")
}

/// Unpacks `archive` next to `exe` and returns the program inside with `exe`'s name.
fn unpack_program(archive_file: &Path, exe: &Path) -> io::Result<PathBuf> {
    let dir = update_dir(exe);
    let _ = fs::remove_dir_all(&dir);
    archive::extract(archive_file, None, &dir, &mut |_| true)?;
    let name = exe.file_name().unwrap_or_default();
    walkdir::WalkDir::new(&dir)
        .into_iter()
        .filter_map(Result::ok)
        .find(|e| e.file_type().is_file() && e.file_name() == name)
        .map(|e| e.into_path())
        .ok_or_else(|| io::Error::other(format!("{} not found in the update", name.display())))
}

/// Swaps the `.app` bundle for the one in the `tar.gz`, keeping the old one until the
/// new one is in place.
fn replace_bundle(archive_file: &Path, bundle: &Path) -> io::Result<()> {
    let dir = update_dir(bundle);
    let _ = fs::remove_dir_all(&dir);
    archive::extract(archive_file, None, &dir, &mut |_| true)?;
    let new = fs::read_dir(&dir)?
        .filter_map(Result::ok)
        .map(|e| e.path())
        .find(|p| p.extension().is_some_and(|x| x == "app"))
        .ok_or_else(|| io::Error::other("no .app in the update"))?;
    let old = sibling(bundle, ".old");
    let _ = fs::remove_dir_all(&old);
    fs::rename(bundle, &old)?;
    if let Err(e) = fs::rename(&new, bundle) {
        let _ = fs::rename(&old, bundle);
        return Err(e);
    }
    let _ = fs::remove_dir_all(&old);
    let _ = fs::remove_dir_all(&dir);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn replace_file_swaps_the_content_and_leaves_no_staging_file() {
        let dir = tempfile::tempdir().unwrap();
        let target = dir.path().join("app");
        let new = dir.path().join("new");
        fs::write(&target, "old").unwrap();
        fs::write(&new, "new").unwrap();
        replace_file(&new, &target).unwrap();
        assert_eq!(fs::read_to_string(&target).unwrap(), "new");
        assert!(!sibling(&target, ".update-new").exists());
        remove_leftovers(&target);
        assert!(!sibling(&target, ".old").exists());
    }

    #[test]
    fn a_portable_update_takes_the_program_from_the_archive() {
        let dir = tempfile::tempdir().unwrap();
        let exe = dir.path().join("bin").join("d3skcommander");
        fs::create_dir_all(exe.parent().unwrap()).unwrap();
        fs::write(&exe, "v1").unwrap();
        let staged = dir.path().join("stage");
        fs::create_dir_all(&staged).unwrap();
        fs::write(staged.join("d3skcommander"), "v2").unwrap();
        let tgz = dir.path().join("u.tar.gz");
        archive::create(
            &tgz,
            archive::ArchiveKind::TarGz,
            &[staged.join("d3skcommander")],
            6,
            &mut |_| true,
        )
        .unwrap();
        let relaunch = install(&InstallKind::Portable(exe.clone()), &tgz).unwrap();
        assert_eq!(relaunch, Relaunch::of(&exe));
        assert_eq!(fs::read_to_string(&exe).unwrap(), "v2");
    }

    #[test]
    fn source_builds_are_not_replaced() {
        assert!(install(&InstallKind::Source, Path::new("x")).is_err());
    }

    #[test]
    fn the_windows_setup_relaunches_silently() {
        let r = install(
            &InstallKind::WindowsSetup("a.exe".into()),
            Path::new("s.exe"),
        )
        .unwrap();
        assert_eq!(r.program, PathBuf::from("s.exe"));
        assert_eq!(r.args, ["/S", "/R"]);
    }
}
