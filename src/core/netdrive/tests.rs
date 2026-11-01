use std::path::{Path, PathBuf};

use super::*;

fn fixture(rel: &str) -> String {
    let path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests/fixtures")
        .join(rel);
    std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("{}: {e}", path.display()))
}

fn share(scheme: &str, user: Option<&str>, server: &str, name: &str, sub: &str) -> Share {
    Share {
        scheme: scheme.into(),
        user: user.map(Into::into),
        server: server.into(),
        share: name.into(),
        sub: sub.into(),
    }
}

#[test]
fn parse_reads_unc_slash_and_url_forms() {
    let media = share("smb", None, "nas", "media", "");
    assert_eq!(parse(r"\\nas\media").unwrap(), media);
    assert_eq!(parse(r"\\nas\media\").unwrap(), media);
    assert_eq!(parse("//nas/media").unwrap(), media);
    assert_eq!(parse("nas/media").unwrap(), media);
    assert_eq!(parse(" smb://nas/media/ ").unwrap(), media);
    assert_eq!(parse("cifs://nas/media").unwrap(), media);
    assert_eq!(
        parse(r"\\nas\media\films\2024").unwrap(),
        share("smb", None, "nas", "media", "films/2024")
    );
    assert_eq!(
        parse("SMB://bob@nas.example.com/media/x").unwrap(),
        share("smb", Some("bob"), "nas.example.com", "media", "x")
    );
    assert_eq!(
        parse("sftp://bob@host.example.com").unwrap(),
        share("sftp", Some("bob"), "host.example.com", "", "")
    );
}

#[test]
fn parse_refuses_what_cannot_be_mounted() {
    assert!(parse("").is_err());
    assert!(parse(r"\\").is_err());
    assert!(parse(r"\\nas").is_err(), "an SMB address needs a share");
    assert!(parse("smb://@/media").is_err());
    assert!(parse("http://example.com/x").is_err());
}

#[test]
fn share_gives_uri_unc_and_inner_folder() {
    let s = parse(r"\\nas\media\films\2024").unwrap();
    assert_eq!(s.uri(), "smb://nas/media/");
    assert_eq!(s.unc(), r"\\nas\media");
    assert_eq!(
        s.inside(Path::new("/mnt/media")),
        Path::new("/mnt/media").join("films").join("2024")
    );
    assert_eq!(parse("sftp://bob@h").unwrap().uri(), "sftp://bob@h/");
}

#[test]
fn applescript_strings_escape_quotes_and_backslashes() {
    assert_eq!(applescript_string(r#"a"b\c"#), r#""a\"b\\c""#);
}

#[test]
fn mac_script_puts_domain_user_in_the_url_and_quotes_the_password() {
    let s = parse(r"\\nas\media").unwrap();
    let creds = Credentials {
        user: "bob".into(),
        domain: "CORP".into(),
        password: r#"p"w"#.into(),
    };
    assert_eq!(
        mac_mount_script(&s, &creds),
        r#"mount volume "smb://CORP;bob@nas/media/" with password "p\"w""#
    );
    assert_eq!(
        mac_mount_script(&s, &Credentials::default()),
        r#"mount volume "smb://nas/media/""#
    );
    assert_eq!(
        mac_mount_script(&parse("davs://h/files").unwrap(), &Credentials::default()),
        r#"mount volume "https://h/files/""#
    );
}

#[test]
fn gio_answers_are_user_domain_password_lines() {
    let creds = Credentials {
        user: "bob".into(),
        domain: String::new(),
        password: "secret".into(),
    };
    assert_eq!(gio_answers(&creds), "bob\n\nsecret\n");
}

#[test]
fn gio_info_gives_the_local_folder_of_a_mounted_share() {
    let out = fixture("gio/2.88/info-smb-mounted.txt");
    assert_eq!(
        gio_local_path(&out),
        Some(PathBuf::from(
            "/run/user/1000/gvfs/smb-share:server=nas.example.com,share=downloads"
        ))
    );
    assert_eq!(
        gio_local_path(&fixture("gio/2.88/info-not-mounted.txt")),
        None
    );
}

#[test]
fn gvfs_folders_map_back_to_their_uri() {
    let base = Path::new("/run/user/1000/gvfs");
    let smb = base.join("smb-share:server=nas.example.com,share=down%2cloads");
    assert_eq!(
        gvfs_uri(&smb.join("sub/dir")).as_deref(),
        Some("smb://nas.example.com/down,loads/")
    );
    assert_eq!(
        gvfs_uri(&base.join("sftp:host=h.example.com,port=2222,user=bob")).as_deref(),
        Some("sftp://bob@h.example.com:2222/")
    );
    assert_eq!(
        gvfs_uri(&base.join("dav:host=d.example.com,ssl=true")).as_deref(),
        Some("davs://d.example.com/")
    );
    assert_eq!(gvfs_uri(Path::new("/home/bob/gvfs-notes")), None);
    assert_eq!(gvfs_uri(Path::new("/media/usb")), None);
}

#[test]
fn mac_mount_output_finds_the_share_and_the_mount_of_a_path() {
    let out = fixture("macos-mount/15/mount.txt");
    let mounts = mac_network_mounts(&out);
    assert_eq!(mounts.len(), 3, "only smbfs/webdav lines: {mounts:?}");
    assert_eq!(
        mac_mount_point(&out, &parse(r"\\nas.example.com\media").unwrap()),
        Some(PathBuf::from("/Volumes/media"))
    );
    assert_eq!(
        mac_mount_point(&out, &parse(r"\\NAS\public share").unwrap()),
        Some(PathBuf::from("/Volumes/Public Share")),
        "short host name, case and %20 in the share name"
    );
    assert_eq!(
        mac_mount_point(&out, &parse(r"\\other\media").unwrap()),
        None
    );
    assert_eq!(
        mac_mount_containing(&out, Path::new("/Volumes/Public Share/docs/a.txt")),
        Some(PathBuf::from("/Volumes/Public Share"))
    );
    assert_eq!(
        mac_mount_containing(&out, Path::new("/Volumes/USB KEY/x")),
        None
    );
}

#[test]
fn windows_roots_and_drive_letters() {
    assert_eq!(
        windows_root(r"\\nas\media\films").as_deref(),
        Some(r"\\nas\media")
    );
    assert_eq!(windows_root("//nas/media").as_deref(), Some(r"\\nas\media"));
    assert_eq!(windows_root(r"z:\films").as_deref(), Some("Z:"));
    assert_eq!(windows_root(r"\\?\C:\x"), None);
    assert_eq!(windows_root("/home/bob"), None);
    assert_eq!(drive_letter("").unwrap(), None);
    assert_eq!(drive_letter("z").unwrap().as_deref(), Some("Z:"));
    assert_eq!(drive_letter(r"Z:\").unwrap().as_deref(), Some("Z:"));
    assert!(drive_letter("ZZ").is_err());
    assert!(drive_letter("1").is_err());
}

/// Manual check against a real server on the current OS:
/// `D3SK_TEST_SHARE='\\nas\share' [D3SK_TEST_USER=… D3SK_TEST_PASSWORD=…] cargo test --lib
/// netdrive -- --ignored`. Leaves the share connected (it may have been before).
#[test]
#[ignore = "needs a real network share (D3SK_TEST_SHARE)"]
fn connects_to_a_real_share() {
    let Ok(address) = std::env::var("D3SK_TEST_SHARE") else {
        return;
    };
    let creds = Credentials {
        user: std::env::var("D3SK_TEST_USER").unwrap_or_default(),
        domain: std::env::var("D3SK_TEST_DOMAIN").unwrap_or_default(),
        password: std::env::var("D3SK_TEST_PASSWORD").unwrap_or_default(),
    };
    let dir = connect(&parse(&address).unwrap(), &creds, None).unwrap();
    assert!(dir.is_dir(), "{} is not a folder", dir.display());
    std::fs::read_dir(&dir).unwrap();
}
