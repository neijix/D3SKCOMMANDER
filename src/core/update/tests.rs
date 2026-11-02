use super::*;

const RIPGREP: &str =
    include_str!("../../../tests/fixtures/github-releases/2022-11-28/ripgrep-latest.json");

fn facts(os: &'static str, exe: &str) -> Facts {
    Facts {
        os,
        exe: PathBuf::from(exe),
        appimage: None,
        dpkg_owned: false,
        has_uninstaller: false,
        in_cargo_target: false,
    }
}

#[test]
fn versions_compare_numerically_and_releases_beat_pre_releases() {
    assert!(is_newer("0.10.0", "0.9.9"));
    assert!(is_newer("v1.0.0", "0.9.0"));
    assert!(is_newer("1.0.0", "1.0.0-beta.2"));
    assert!(is_newer("1.0.0-rc.1", "1.0.0-beta.2"));
    assert!(!is_newer("1.0.0", "1.0.0"));
    assert!(!is_newer("0.9.0", "1.0.0"));
    assert!(!is_newer("nightly", "1.0.0"));
    assert_eq!(Version::parse("2"), Version::parse("2.0.0"));
    assert_eq!(Version::parse("1.2.3.4"), None);
}

#[test]
fn the_api_url_comes_from_the_repository_address() {
    assert_eq!(
        latest_release_url("https://github.com/neijix/d3skcommander/").as_deref(),
        Some("https://api.github.com/repos/neijix/d3skcommander/releases/latest")
    );
    assert_eq!(latest_release_url("https://gitlab.com/a/b"), None);
    assert_eq!(latest_release_url("https://github.com/a"), None);
    assert!(latest_release_url(REPOSITORY).is_some());
}

#[test]
fn a_recorded_github_release_is_read() {
    let r = parse_release(RIPGREP).unwrap();
    assert_eq!(r.version, "15.2.0");
    assert!(
        r.page
            .starts_with("https://github.com/BurntSushi/ripgrep/releases/")
    );
    assert!(!r.notes.contains('\r'));
    assert_eq!(r.assets.len(), 28);
    let a = &r.assets[0];
    assert_eq!(a.name, "ripgrep-15.2.0-aarch64-apple-darwin.tar.gz");
    assert!(a.url.starts_with("https://github.com/"));
    assert_eq!(a.size, 1_764_284);
    assert_eq!(
        a.sha256.as_deref(),
        Some("3750b2e93f37e0c692657da574d7019a101c0084da05a790c83fd335bad973e4")
    );
}

#[test]
fn drafts_and_pre_releases_are_refused() {
    let pre = RIPGREP.replacen("\"prerelease\":false", "\"prerelease\":true", 1);
    assert!(parse_release(&pre).is_err());
    assert!(parse_release("{}").is_err());
}

#[test]
fn the_install_kind_follows_where_the_program_lives() {
    let f = facts("linux", "/home/u/Apps/d3skcommander");
    assert_eq!(detect(&f), InstallKind::Portable(f.exe.clone()));

    let mut img = f.clone();
    img.appimage = Some("/home/u/Apps/d3sk.AppImage".into());
    assert_eq!(
        detect(&img),
        InstallKind::AppImage("/home/u/Apps/d3sk.AppImage".into())
    );

    let mut deb = facts("linux", "/usr/bin/d3skcommander");
    deb.dpkg_owned = true;
    assert_eq!(detect(&deb), InstallKind::Deb(deb.exe.clone()));

    let mac = facts(
        "macos",
        "/Applications/d3skcommander.app/Contents/MacOS/d3skcommander",
    );
    assert_eq!(
        detect(&mac),
        InstallKind::MacApp("/Applications/d3skcommander.app".into())
    );

    let mut win = facts(
        "windows",
        r"C:\Users\u\AppData\Local\d3skcommander\d3skcommander.exe",
    );
    assert_eq!(detect(&win), InstallKind::Portable(win.exe.clone()));
    win.has_uninstaller = true;
    assert_eq!(detect(&win), InstallKind::WindowsSetup(win.exe.clone()));

    let mut src = deb.clone();
    src.in_cargo_target = true;
    assert_eq!(detect(&src), InstallKind::Source);
}

#[test]
fn each_install_kind_picks_its_own_release_file() {
    let asset = |name: &str| Asset {
        name: name.into(),
        url: format!("https://example.invalid/{name}"),
        size: 1,
        sha256: None,
    };
    let release = Release {
        version: "1.0.0".into(),
        notes: String::new(),
        page: String::new(),
        assets: [
            "d3skcommander-1.0.0-linux-x86_64.deb",
            "d3skcommander-1.0.0-linux-x86_64.AppImage",
            "d3skcommander-1.0.0-linux-x86_64.tar.gz",
            "d3skcommander-1.0.0-macos-aarch64.dmg",
            "d3skcommander-1.0.0-macos-aarch64.app.tar.gz",
            "d3skcommander-1.0.0-windows-x86_64-setup.exe",
            "d3skcommander-1.0.0-windows-x86_64.zip",
            "SHA256SUMS",
        ]
        .map(asset)
        .to_vec(),
    };
    let name = |kind: InstallKind, os: &str, arch: &str| {
        pick_asset(&release, &kind, os, arch).map(|a| a.name.clone())
    };
    let p = PathBuf::new;
    assert_eq!(
        name(InstallKind::Deb(p()), "linux", "x86_64").unwrap(),
        "d3skcommander-1.0.0-linux-x86_64.deb"
    );
    assert!(
        name(InstallKind::AppImage(p()), "linux", "x86_64")
            .unwrap()
            .ends_with(".AppImage")
    );
    assert!(
        name(InstallKind::Portable(p()), "linux", "x86_64")
            .unwrap()
            .ends_with("linux-x86_64.tar.gz")
    );
    assert!(
        name(InstallKind::MacApp(p()), "macos", "aarch64")
            .unwrap()
            .ends_with(".app.tar.gz")
    );
    assert!(
        name(InstallKind::WindowsSetup(p()), "windows", "x86_64")
            .unwrap()
            .ends_with("-setup.exe")
    );
    assert!(
        name(InstallKind::Portable(p()), "windows", "x86_64")
            .unwrap()
            .ends_with(".zip")
    );
    assert_eq!(name(InstallKind::Deb(p()), "linux", "aarch64"), None);
    assert_eq!(name(InstallKind::Source, "linux", "x86_64"), None);
}
