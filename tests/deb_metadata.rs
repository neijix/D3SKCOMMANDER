//! The text Ubuntu App Center shows for the .deb comes from Cargo.toml and the
//! AppStream file. The release version in that file has to match the crate.

use std::fs;

#[test]
fn deb_metadata_matches_the_crate_version() {
    let root = env!("CARGO_MANIFEST_DIR");
    let manifest = fs::read_to_string(format!("{root}/Cargo.toml")).unwrap();
    let version = manifest
        .lines()
        .find_map(|line| {
            line.strip_prefix("version = \"")
                .map(|rest| rest.trim_end_matches('"').to_string())
        })
        .expect("package version");
    let meta = fs::read_to_string(format!(
        "{root}/packaging/linux/io.github.neijix.d3skcommander.metainfo.xml"
    ))
    .unwrap();
    assert!(
        meta.contains(&format!("version=\"{version}\"")),
        "metainfo release version must be {version}"
    );
    assert!(meta.contains("<project_license>MIT</project_license>"));
    assert!(meta.contains("<name>neijix</name>"));
    assert!(manifest.contains("longDescription = \"\"\""));
    assert!(manifest.contains("asks where to keep your settings"));
    assert!(!meta.contains("(none)"));
}
