//! Self-update, the rules: which version is newer, how this copy was installed and which
//! release file updates it. The network and the file swap are in [`apply`].
//!
//! Releases live on the GitHub repository named in `Cargo.toml` (`repository`); the
//! release workflow names every file `d3skcommander-<version>-<os>-<arch><suffix>`, and
//! [`asset_suffix`] is the other half of that contract.

pub mod apply;

use std::cmp::Ordering;
use std::path::{Path, PathBuf};

use serde::Deserialize;

pub const CURRENT_VERSION: &str = env!("CARGO_PKG_VERSION");
pub const REPOSITORY: &str = env!("CARGO_PKG_REPOSITORY");

/// The API address of the latest release of `repo` (`https://github.com/<owner>/<name>`).
pub fn latest_release_url(repo: &str) -> Option<String> {
    let rest = repo
        .trim_end_matches('/')
        .strip_prefix("https://github.com/")?;
    let mut parts = rest.split('/');
    let (owner, name) = (parts.next()?, parts.next()?);
    if owner.is_empty() || name.is_empty() || parts.next().is_some() {
        return None;
    }
    Some(format!(
        "https://api.github.com/repos/{owner}/{name}/releases/latest"
    ))
}

/// `major.minor.patch[-pre]`, with or without a leading `v`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Version {
    nums: [u64; 3],
    pre: Option<String>,
}

impl Version {
    pub fn parse(text: &str) -> Option<Version> {
        let text = text.trim().trim_start_matches(['v', 'V']);
        let text = text.split('+').next()?;
        let (core, pre) = match text.split_once('-') {
            Some((c, p)) => (c, Some(p.to_string())),
            None => (text, None),
        };
        let mut nums = [0u64; 3];
        let mut count = 0;
        for (slot, part) in nums.iter_mut().zip(core.split('.')) {
            *slot = part.parse().ok()?;
            count += 1;
        }
        (count > 0 && core.split('.').count() <= 3).then_some(Version { nums, pre })
    }
}

impl Ord for Version {
    fn cmp(&self, other: &Self) -> Ordering {
        self.nums
            .cmp(&other.nums)
            .then_with(|| match (&self.pre, &other.pre) {
                (None, None) => Ordering::Equal,
                (None, Some(_)) => Ordering::Greater,
                (Some(_), None) => Ordering::Less,
                (Some(a), Some(b)) => a.cmp(b),
            })
    }
}

impl PartialOrd for Version {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

/// True when `candidate` is a later version than `current` (unreadable = not newer).
pub fn is_newer(candidate: &str, current: &str) -> bool {
    match (Version::parse(candidate), Version::parse(current)) {
        (Some(a), Some(b)) => a > b,
        _ => false,
    }
}

/// One downloadable file of a release.
#[derive(Clone, Debug, PartialEq)]
pub struct Asset {
    pub name: String,
    pub url: String,
    pub size: u64,
    /// Lower-case hex SHA-256 computed by GitHub when the file was uploaded.
    pub sha256: Option<String>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Release {
    pub version: String,
    pub notes: String,
    /// The release's web page.
    pub page: String,
    pub assets: Vec<Asset>,
}

#[derive(Deserialize)]
struct ApiRelease {
    tag_name: String,
    html_url: String,
    #[serde(default)]
    body: Option<String>,
    #[serde(default)]
    draft: bool,
    #[serde(default)]
    prerelease: bool,
    #[serde(default)]
    assets: Vec<ApiAsset>,
}

#[derive(Deserialize)]
struct ApiAsset {
    name: String,
    browser_download_url: String,
    size: u64,
    #[serde(default)]
    digest: Option<String>,
    #[serde(default)]
    state: Option<String>,
}

/// Reads a GitHub "release" API answer. Drafts, pre-releases and unreadable tags are
/// refused; only fully uploaded files are kept.
pub fn parse_release(json: &str) -> Result<Release, String> {
    let r: ApiRelease = serde_json::from_str(json).map_err(|e| e.to_string())?;
    if r.draft || r.prerelease {
        return Err(format!("{} is not a final release", r.tag_name));
    }
    let version = Version::parse(&r.tag_name)
        .map(|_| r.tag_name.trim_start_matches(['v', 'V']).to_string())
        .ok_or_else(|| format!("unreadable version: {}", r.tag_name))?;
    let assets = r
        .assets
        .into_iter()
        .filter(|a| a.state.as_deref().is_none_or(|s| s == "uploaded"))
        .map(|a| Asset {
            sha256: a
                .digest
                .as_deref()
                .and_then(|d| d.strip_prefix("sha256:"))
                .map(str::to_ascii_lowercase),
            name: a.name,
            url: a.browser_download_url,
            size: a.size,
        })
        .collect();
    Ok(Release {
        version,
        notes: r.body.unwrap_or_default().replace("\r\n", "\n"),
        page: r.html_url,
        assets,
    })
}

/// How this copy of the app was installed, which decides how it updates itself.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum InstallKind {
    /// A Linux AppImage file (`$APPIMAGE`): replaced by the new AppImage.
    AppImage(PathBuf),
    /// Installed from the `.deb` (dpkg owns the binary): the new `.deb` is installed
    /// with administrator rights (pkexec).
    Deb(PathBuf),
    /// A macOS `.app` bundle: replaced by the new bundle.
    MacApp(PathBuf),
    /// Installed by the Windows setup (an `uninstall.exe` beside the program): the new
    /// setup runs silently and restarts the app.
    WindowsSetup(PathBuf),
    /// A program file copied by hand (release archive): replaced in place.
    Portable(PathBuf),
    /// Built from a source checkout (`target/…`): update with git and cargo instead.
    Source,
}

/// What [`detect`] looks at, gathered by [`Facts::current`].
#[derive(Clone, Debug)]
pub struct Facts {
    pub os: &'static str,
    pub exe: PathBuf,
    pub appimage: Option<PathBuf>,
    /// dpkg lists the program as part of the installed package.
    pub dpkg_owned: bool,
    /// An `uninstall.exe` sits beside the program.
    pub has_uninstaller: bool,
    /// The program lies in a cargo `target` folder next to a `Cargo.toml`.
    pub in_cargo_target: bool,
}

impl Facts {
    pub fn current() -> Facts {
        let exe = std::env::current_exe()
            .and_then(|p| p.canonicalize())
            .unwrap_or_default();
        let dir = exe.parent().unwrap_or(Path::new(""));
        Facts {
            os: std::env::consts::OS,
            appimage: std::env::var_os("APPIMAGE").map(PathBuf::from),
            dpkg_owned: dpkg_owns(&exe),
            has_uninstaller: dir.join("uninstall.exe").is_file(),
            in_cargo_target: exe.ancestors().any(|a| {
                a.file_name().is_some_and(|n| n == "target")
                    && a.parent().is_some_and(|p| p.join("Cargo.toml").is_file())
            }),
            exe,
        }
    }
}

fn dpkg_owns(exe: &Path) -> bool {
    let name = exe.to_string_lossy();
    std::fs::read_to_string("/var/lib/dpkg/info/d3skcommander.list")
        .is_ok_and(|list| list.lines().any(|l| l == name))
}

pub fn detect(f: &Facts) -> InstallKind {
    if f.in_cargo_target {
        return InstallKind::Source;
    }
    if f.os == "linux"
        && let Some(p) = &f.appimage
    {
        return InstallKind::AppImage(p.clone());
    }
    if f.os == "macos"
        && let Some(bundle) = f
            .exe
            .ancestors()
            .find(|a| a.extension().is_some_and(|x| x == "app"))
    {
        return InstallKind::MacApp(bundle.to_path_buf());
    }
    if f.os == "linux" && f.dpkg_owned {
        return InstallKind::Deb(f.exe.clone());
    }
    if f.os == "windows" && f.has_uninstaller {
        return InstallKind::WindowsSetup(f.exe.clone());
    }
    InstallKind::Portable(f.exe.clone())
}

/// The end of the release file name that updates `kind` (`None`: no file can).
pub fn asset_suffix(kind: &InstallKind, os: &str, arch: &str) -> Option<String> {
    Some(match kind {
        InstallKind::AppImage(_) => format!("-linux-{arch}.AppImage"),
        InstallKind::Deb(_) => format!("-linux-{arch}.deb"),
        InstallKind::MacApp(_) => format!("-macos-{arch}.app.tar.gz"),
        InstallKind::WindowsSetup(_) => format!("-windows-{arch}-setup.exe"),
        InstallKind::Portable(_) if os == "windows" => format!("-windows-{arch}.zip"),
        InstallKind::Portable(_) => format!("-{os}-{arch}.tar.gz"),
        InstallKind::Source => return None,
    })
}

/// The release file that updates `kind` on this `os`/`arch`.
pub fn pick_asset<'a>(
    release: &'a Release,
    kind: &InstallKind,
    os: &str,
    arch: &str,
) -> Option<&'a Asset> {
    let suffix = asset_suffix(kind, os, arch)?;
    release.assets.iter().find(|a| a.name.ends_with(&suffix))
}

#[cfg(test)]
mod tests;
