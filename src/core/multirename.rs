//! Multi-rename engine: build new names from masks (placeholders, counter, search/replace,
//! case mode), preview them with conflict detection, then apply them safely (swaps and chains
//! go through temporary names, a failure rolls back) and undo.

use std::collections::{HashMap, HashSet};
use std::fs;
use std::path::{Path, PathBuf};

use regex::{NoExpand, RegexBuilder};

mod placeholder;

use placeholder::{Ctx, expand};

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, serde::Serialize, serde::Deserialize)]
pub enum CaseMode {
    #[default]
    Unchanged,
    Lower,
    Upper,
    FirstUpper,
    TitleCase,
}

#[derive(Clone, Debug, serde::Serialize, serde::Deserialize)]
pub struct RenameRule {
    pub name_mask: String,
    pub ext_mask: String,
    pub search: String,
    pub replace: String,
    pub use_regex: bool,
    pub case_sensitive: bool,
    pub case_mode: CaseMode,
    pub counter_start: i64,
    pub counter_step: i64,
    pub counter_digits: usize,
}

impl Default for RenameRule {
    fn default() -> Self {
        Self {
            name_mask: "[N]".into(),
            ext_mask: "[E]".into(),
            search: String::new(),
            replace: String::new(),
            use_regex: false,
            case_sensitive: true,
            case_mode: CaseMode::Unchanged,
            counter_start: 1,
            counter_step: 1,
            counter_digits: 1,
        }
    }
}

#[derive(Clone, Debug)]
pub struct RenamePreview {
    pub from: PathBuf,
    pub to: PathBuf,
    pub error: Option<String>,
}

fn search_replace(name: String, rule: &RenameRule) -> Result<String, String> {
    if rule.search.is_empty() {
        return Ok(name);
    }
    let pattern = if rule.use_regex {
        rule.search.clone()
    } else {
        regex::escape(&rule.search)
    };
    let re = RegexBuilder::new(&pattern)
        .case_insensitive(!rule.case_sensitive)
        .build()
        .map_err(|e| format!("invalid search pattern: {e}"))?;
    let replaced = if rule.use_regex {
        re.replace_all(&name, rule.replace.as_str())
    } else {
        re.replace_all(&name, NoExpand(&rule.replace))
    };
    Ok(replaced.into_owned())
}

fn apply_case(name: &str, mode: CaseMode) -> String {
    match mode {
        CaseMode::Unchanged => name.to_string(),
        CaseMode::Lower => name.to_lowercase(),
        CaseMode::Upper => name.to_uppercase(),
        CaseMode::FirstUpper => {
            let mut chars = name.chars();
            match chars.next() {
                Some(c) => c
                    .to_uppercase()
                    .chain(chars.flat_map(char::to_lowercase))
                    .collect(),
                None => String::new(),
            }
        }
        CaseMode::TitleCase => {
            let mut out = String::new();
            let mut in_word = false;
            for c in name.chars() {
                if in_word {
                    out.extend(c.to_lowercase());
                } else {
                    out.extend(c.to_uppercase());
                }
                in_word = c.is_alphanumeric() || (in_word && c == '\'');
            }
            out
        }
    }
}

/// The new file name (no directory) for `file`, which is the `index`-th (0-based) of the batch.
pub fn new_name(file: &Path, index: usize, rule: &RenameRule) -> Result<String, String> {
    let lossy = |s: Option<&std::ffi::OsStr>| {
        s.map(|s| s.to_string_lossy().into_owned())
            .unwrap_or_default()
    };
    let ctx = Ctx {
        file,
        index,
        rule,
        stem: lossy(file.file_stem()),
        ext: lossy(file.extension()),
    };
    let name = expand(&rule.name_mask, &ctx)?;
    let ext = expand(&rule.ext_mask, &ctx)?;
    let full = if ext.is_empty() {
        name
    } else {
        format!("{name}.{ext}")
    };
    Ok(apply_case(&search_replace(full, rule)?, rule.case_mode))
}

/// Why `name` cannot be a file name on every platform, if it cannot.
fn invalid_name(name: &str) -> Option<String> {
    if name.is_empty() {
        return Some("the new name is empty".into());
    }
    if let Some(c) = name
        .chars()
        .find(|c| "<>:\"/\\|?*".contains(*c) || c.is_control())
    {
        return Some(format!(
            "the new name contains an invalid character ({c:?})"
        ));
    }
    if name.ends_with('.') || name.ends_with(' ') {
        return Some("the new name ends with a dot or a space".into());
    }
    None
}

/// Comparison key: case-insensitive where the usual file systems are.
fn key(path: &Path) -> String {
    let s = path.to_string_lossy();
    if cfg!(any(windows, target_os = "macos")) {
        s.to_lowercase()
    } else {
        s.into_owned()
    }
}

fn same_file(a: &Path, b: &Path) -> bool {
    matches!((fs::canonicalize(a), fs::canonicalize(b)), (Ok(x), Ok(y)) if x == y)
}

/// Compute every new name and flag what cannot be applied: invalid names, duplicates inside the
/// batch, targets that exist outside the batch.
pub fn preview(files: &[PathBuf], rule: &RenameRule) -> Vec<RenamePreview> {
    let mut out: Vec<RenamePreview> = files
        .iter()
        .enumerate()
        .map(|(i, from)| match new_name(from, i, rule) {
            Ok(name) => RenamePreview {
                to: from.with_file_name(&name),
                error: invalid_name(&name),
                from: from.clone(),
            },
            Err(e) => RenamePreview {
                from: from.clone(),
                to: from.clone(),
                error: Some(e),
            },
        })
        .collect();

    let mut counts: HashMap<String, usize> = HashMap::new();
    for p in out.iter().filter(|p| p.error.is_none()) {
        *counts.entry(key(&p.to)).or_default() += 1;
    }
    let sources: HashSet<String> = files.iter().map(|f| key(f)).collect();
    for p in out.iter_mut().filter(|p| p.error.is_none()) {
        if counts.get(&key(&p.to)).copied().unwrap_or(0) > 1 {
            p.error = Some("another file in the batch gets the same name".into());
        } else if p.to != p.from
            && !sources.contains(&key(&p.to))
            && fs::symlink_metadata(&p.to).is_ok()
            && !same_file(&p.from, &p.to)
        {
            p.error = Some("a file with the new name already exists".into());
        }
    }
    out
}

fn rename_checked(from: &Path, to: &Path, log: &mut Vec<(PathBuf, PathBuf)>) -> Result<(), String> {
    if fs::symlink_metadata(to).is_ok() && !same_file(from, to) {
        return Err(format!("{} already exists", to.display()));
    }
    fs::rename(from, to)
        .map_err(|e| format!("cannot rename {} to {}: {e}", from.display(), to.display()))?;
    log.push((from.to_path_buf(), to.to_path_buf()));
    Ok(())
}

fn temp_name(path: &Path, n: usize) -> PathBuf {
    let mut k = n;
    loop {
        let name = path
            .file_name()
            .map(|s| s.to_string_lossy().into_owned())
            .unwrap_or_default();
        let candidate = path.with_file_name(format!(".{name}.d3sk-tmp-{k}"));
        if fs::symlink_metadata(&candidate).is_err() {
            return candidate;
        }
        k += 1;
    }
}

/// Undo the raw renames in `log`, newest first. Returns the paths that could not be restored.
fn rollback(log: &[(PathBuf, PathBuf)]) -> Vec<String> {
    log.iter()
        .rev()
        .filter(|(from, to)| fs::rename(to, from).is_err())
        .map(|(from, _)| from.display().to_string())
        .collect()
}

/// Rename every `(from, to)` pair. A source that is also somebody's target goes through a
/// temporary name first, so swaps and chains work; on error everything done is rolled back.
fn execute(pairs: &[(PathBuf, PathBuf)]) -> Result<Vec<(PathBuf, PathBuf)>, String> {
    let moving: Vec<&(PathBuf, PathBuf)> = pairs.iter().filter(|(f, t)| f != t).collect();
    let targets: HashSet<&Path> = moving.iter().map(|(_, t)| t.as_path()).collect();
    let mut log = Vec::new();
    let mut run = || -> Result<(), String> {
        let (mut direct, mut staged) = (Vec::new(), Vec::new());
        for (n, (from, to)) in moving.iter().map(|p| (&p.0, &p.1)).enumerate() {
            if targets.contains(from.as_path()) {
                let tmp = temp_name(from, n);
                rename_checked(from, &tmp, &mut log)?;
                staged.push((tmp, to));
            } else {
                direct.push((from.clone(), to));
            }
        }
        for (from, to) in direct.iter().chain(staged.iter()) {
            rename_checked(from, to, &mut log)?;
        }
        Ok(())
    };
    match run() {
        Ok(()) => Ok(moving.into_iter().cloned().collect()),
        Err(e) => {
            let stuck = rollback(&log);
            if stuck.is_empty() {
                Err(format!("{e} (rolled back)"))
            } else {
                Err(format!("{e} (rollback failed for: {})", stuck.join(", ")))
            }
        }
    }
}

/// Apply the previews. Refuses (touching nothing) when one carries an error; unchanged names are
/// skipped. Returns the `(from, to)` pairs actually renamed, for `undo`.
pub fn apply(previews: &[RenamePreview]) -> Result<Vec<(PathBuf, PathBuf)>, String> {
    if let Some(p) = previews.iter().find(|p| p.error.is_some()) {
        let msg = p.error.as_deref().unwrap_or_default();
        return Err(format!("{}: {msg}", p.from.display()));
    }
    let pairs: Vec<_> = previews
        .iter()
        .map(|p| (p.from.clone(), p.to.clone()))
        .collect();
    execute(&pairs)
}

/// Reverse a previous `apply`.
pub fn undo(done: &[(PathBuf, PathBuf)]) -> Result<(), String> {
    let pairs: Vec<_> = done
        .iter()
        .map(|(from, to)| (to.clone(), from.clone()))
        .collect();
    execute(&pairs).map(|_| ())
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::{Local, TimeZone};
    use tempfile::tempdir;

    #[rustfmt::skip]
    fn rule(name: &str, ext: &str) -> RenameRule {
        RenameRule { name_mask: name.into(), ext_mask: ext.into(), ..RenameRule::default() }
    }

    #[rustfmt::skip]
    fn mk(from: &Path, to: &Path) -> RenamePreview {
        RenamePreview { from: from.into(), to: to.into(), error: None }
    }

    #[rustfmt::skip]
    fn sr(search: &str, replace: &str, use_regex: bool) -> RenameRule {
        RenameRule { search: search.into(), replace: replace.into(), use_regex, ..RenameRule::default() }
    }

    fn nn(file: &str, idx: usize, r: &RenameRule) -> String {
        new_name(Path::new(file), idx, r).unwrap()
    }

    fn touch(dir: &Path, name: &str) -> PathBuf {
        let p = dir.join(name);
        fs::write(&p, name).unwrap();
        p
    }

    #[test]
    fn name_and_extension_placeholders() {
        assert_eq!(
            nn("/x/photo.JPG", 0, &rule("img_[N]", "[E]")),
            "img_photo.JPG"
        );
        assert_eq!(nn("/x/photo.jpg", 0, &rule("[N]", "")), "photo");
        assert_eq!(nn("/x/photo.jpg", 0, &rule("[N]", "bak")), "photo.bak");
        assert_eq!(nn("/x/a.tar.gz", 0, &RenameRule::default()), "a.tar.gz");
        assert_eq!(nn("/x/noext", 0, &RenameRule::default()), "noext");
    }

    #[test]
    fn char_ranges_are_one_based_and_inclusive() {
        let r = rule("[N3]-[N2-4]-[N3-]-[N9]", "[E1]");
        assert_eq!(nn("/x/abcdef.xyz", 0, &r), "c-bcd-cdef-.x");
        assert_eq!(nn("/x/abc.q", 0, &rule("[N2-99]", "")), "bc");
    }

    #[test]
    fn char_ranges_count_unicode_chars_not_bytes() {
        let r = rule("[N2]|[N2-3]|[N3-]", "[E2-]");
        assert_eq!(nn("/x/héllo→wörld.日本語", 0, &r), "é|él|llo→wörld.本語");
    }

    #[test]
    fn invalid_ranges_and_placeholders_are_errors() {
        for mask in [
            "[N0]", "[N5-2]", "[Nx]", "[N-3]", "[Q]", "[n]", "[N", "[C1+x]", "[Cabc]",
        ] {
            let e = new_name(Path::new("/x/a.txt"), 0, &rule(mask, "[E]"));
            assert!(e.is_err(), "{mask} should fail");
        }
    }

    #[test]
    fn literal_bracket() {
        assert_eq!(nn("/x/a.txt", 0, &rule("[[[N]]", "[E]")), "[a].txt");
        assert_eq!(nn("/x/a.txt", 0, &rule("x[[y", "[E]")), "x[y.txt");
    }

    #[test]
    fn counter_default_padding_step_and_inline_form() {
        let mut r = rule("f[C]", "[E]");
        r.counter_start = 5;
        r.counter_step = 2;
        r.counter_digits = 3;
        assert_eq!(nn("/x/a.txt", 0, &r), "f005.txt");
        assert_eq!(nn("/x/a.txt", 3, &r), "f011.txt");
        let r = rule("[C10+5:3]_[C7]_[C+3]_[C:2]_[C2+:4]", "");
        assert_eq!(nn("/x/a", 2, &r), "020_9_7_03_0004");
        let mut neg = rule("[C]", "");
        neg.counter_start = 10;
        neg.counter_step = -4;
        neg.counter_digits = 3;
        assert_eq!(nn("/x/a", 3, &neg), "-02");
        assert_eq!(nn("/x/a", 1, &rule("[C-5+-2]", "")), "-7");
    }

    #[test]
    fn parent_and_grandparent() {
        let r = rule("[G]_[P]_[N]", "[E]");
        assert_eq!(nn("/top/mid/low/a.txt", 0, &r), "mid_low_a.txt");
        assert_eq!(nn("a.txt", 0, &r), "__a.txt");
    }

    #[test]
    fn date_placeholders_use_local_modification_time() {
        let dir = tempdir().unwrap();
        let f = touch(dir.path(), "a.txt");
        let dt = Local.with_ymd_and_hms(2023, 7, 4, 9, 5, 3).unwrap();
        filetime::set_file_mtime(&f, filetime::FileTime::from_unix_time(dt.timestamp(), 0))
            .unwrap();
        let r = rule("[Y]-[M]-[D] [h].[m].[s]|[YMD]|[hms]", "[E]");
        assert_eq!(
            new_name(&f, 0, &r).unwrap(),
            "2023-07-04 09.05.03|20230704|090503.txt"
        );
        assert!(new_name(&dir.path().join("missing"), 0, &r).is_err());
    }

    #[test]
    fn literal_search_replace() {
        let mut r = sr(".txt", "$1.md", false);
        assert_eq!(nn("/x/atxt.txt", 0, &r), "atxt$1.md");
        r = sr("A", "b", false);
        assert_eq!(nn("/x/aAa.txt", 0, &r), "aba.txt");
        r.case_sensitive = false;
        assert_eq!(nn("/x/aAa.txt", 0, &r), "bbb.txt");
    }

    #[test]
    fn regex_replace_with_capture_groups() {
        let r = sr(r"^(\w+)_(\d+)", "$2-$1", true);
        assert_eq!(nn("/x/img_042.jpg", 0, &r), "042-img.jpg");
        assert!(new_name(Path::new("/x/a"), 0, &sr("(", "", true)).is_err());
        let mut ci = sr("IMG", "p", true);
        ci.case_sensitive = false;
        assert_eq!(nn("/x/img1.jpg", 0, &ci), "p1.jpg");
    }

    #[test]
    fn search_runs_on_full_name_after_masks_and_before_case() {
        let mut r = sr("X.TXT", "-end.dat", false);
        r.name_mask = "[N]x".into();
        r.case_sensitive = false;
        r.case_mode = CaseMode::Upper;
        assert_eq!(nn("/x/a.txt", 0, &r), "A-END.DAT");
        assert_eq!(nn("/x/a.txt", 0, &rule("[N]", "")), "a");
    }

    #[test]
    fn case_modes() {
        let c = |file: &str, mode| {
            let r = RenameRule {
                case_mode: mode,
                ..RenameRule::default()
            };
            nn(file, 0, &r)
        };
        assert_eq!(c("/x/Hello wORLD.TxT", CaseMode::Lower), "hello world.txt");
        assert_eq!(c("/x/Hello wORLD.TxT", CaseMode::Upper), "HELLO WORLD.TXT");
        assert_eq!(
            c("/x/hELLO wORLD.TxT", CaseMode::FirstUpper),
            "Hello world.txt"
        );
        assert_eq!(
            c("/x/hELLO wORLD-it's_é.TxT", CaseMode::TitleCase),
            "Hello World-It's_É.Txt"
        );
        assert_eq!(c("/x/ÉCOLE.txt", CaseMode::Lower), "école.txt");
    }

    #[test]
    fn preview_builds_targets_in_the_same_directory() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a.txt");
        let b = touch(dir.path(), "b.txt");
        let r = rule("n[C:2]", "[E]");
        let p = preview(&[a.clone(), b.clone()], &r);
        assert_eq!(p[0].to, dir.path().join("n01.txt"));
        assert_eq!(p[1].to, dir.path().join("n02.txt"));
        assert!(p.iter().all(|x| x.error.is_none()));
        let same = preview(std::slice::from_ref(&a), &RenameRule::default());
        assert!(same[0].error.is_none() && same[0].to == a);
    }

    #[test]
    fn preview_flags_duplicates_in_batch() {
        let dir = tempdir().unwrap();
        let files = [
            touch(dir.path(), "a.txt"),
            touch(dir.path(), "b.txt"),
            touch(dir.path(), "c.txt"),
        ];
        let p = preview(&files, &rule("same", "[E]"));
        assert!(p.iter().all(|x| x.error.is_some()));
        let p = preview(&files, &rule("c", "txt"));
        assert!(p[0].error.is_some() && p[1].error.is_some() && p[2].error.is_some());
    }

    #[test]
    fn preview_flags_existing_target_outside_batch_but_not_inside() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a.txt");
        touch(dir.path(), "z.txt");
        let p = preview(std::slice::from_ref(&a), &sr("a", "z", false));
        assert!(p[0].error.as_deref().unwrap().contains("exists"));
        // 1.txt -> 2.txt is fine: 2.txt is part of the batch and moves away
        let one = touch(dir.path(), "1.txt");
        let two = touch(dir.path(), "2.txt");
        let shift = rule("[C2]", "[E]");
        let q = preview(&[one, two], &shift);
        assert!(q.iter().all(|x| x.error.is_none()), "{q:?}");
    }

    #[test]
    fn preview_flags_invalid_names_and_bad_masks() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a.txt");
        let files = std::slice::from_ref(&a);
        for (name, ext) in [
            ("", ""),
            ("a:b", "[E]"),
            ("a?", "[E]"),
            ("a|b", "[E]"),
            ("a ", ""),
            ("a.", ""),
            ("a\"", ""),
        ] {
            let p = preview(files, &rule(name, ext));
            assert!(p[0].error.is_some(), "{name:?}.{ext:?} should be invalid");
        }
        let p = preview(files, &rule("[Q]", ""));
        assert!(
            p[0].error
                .as_deref()
                .unwrap()
                .contains("unknown placeholder")
        );
        assert_eq!(p[0].to, a);
    }

    #[test]
    fn apply_renames_and_undo_restores() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a.txt");
        let b = touch(dir.path(), "b.txt");
        let unchanged = touch(dir.path(), "keep.txt");
        let p = preview(&[a.clone(), b.clone(), unchanged], &rule("f[C]", "[E]"));
        let p: Vec<_> = p.into_iter().take(2).collect();
        let done = apply(&p).unwrap();
        assert_eq!(done.len(), 2);
        assert!(!a.exists() && !b.exists());
        assert_eq!(
            fs::read_to_string(dir.path().join("f1.txt")).unwrap(),
            "a.txt"
        );
        undo(&done).unwrap();
        assert_eq!(fs::read_to_string(&a).unwrap(), "a.txt");
        assert_eq!(fs::read_to_string(&b).unwrap(), "b.txt");
        assert!(!dir.path().join("f1.txt").exists());
    }

    #[test]
    fn apply_skips_unchanged_names() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a.txt");
        let done = apply(&preview(std::slice::from_ref(&a), &RenameRule::default())).unwrap();
        assert!(done.is_empty() && a.exists());
    }

    #[test]
    fn apply_swaps_two_files() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a");
        let b = touch(dir.path(), "b");
        let p = vec![mk(&a, &b), mk(&b, &a)];
        let done = apply(&p).unwrap();
        assert_eq!(fs::read_to_string(&a).unwrap(), "b");
        assert_eq!(fs::read_to_string(&b).unwrap(), "a");
        assert_eq!(
            fs::read_dir(dir.path()).unwrap().count(),
            2,
            "no temp file left"
        );
        undo(&done).unwrap();
        assert_eq!(fs::read_to_string(&a).unwrap(), "a");
        assert_eq!(fs::read_to_string(&b).unwrap(), "b");
    }

    #[test]
    fn apply_handles_a_chain_through_the_preview() {
        let dir = tempdir().unwrap();
        let files: Vec<PathBuf> = ["1.txt", "2.txt", "3.txt"]
            .iter()
            .map(|n| touch(dir.path(), n))
            .collect();
        // 1 -> 2, 2 -> 3, 3 -> 4 : every target but the last is another source
        let r = rule("[C2]", "[E]");
        let p = preview(&files, &r);
        assert!(p.iter().all(|x| x.error.is_none()), "{p:?}");
        apply(&p).unwrap();
        assert!(!dir.path().join("1.txt").exists());
        for (n, want) in [("2.txt", "1.txt"), ("3.txt", "2.txt"), ("4.txt", "3.txt")] {
            assert_eq!(fs::read_to_string(dir.path().join(n)).unwrap(), want);
        }
        assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 3);
    }

    #[test]
    fn apply_rolls_back_on_error() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a");
        let b = touch(dir.path(), "b");
        let missing = dir.path().join("missing");
        let c = touch(dir.path(), "c");
        // a -> a2 and b -> b2 succeed, then the missing source fails: all must be restored
        let d = dir.path();
        let p = vec![
            mk(&a, &d.join("a2")),
            mk(&b, &d.join("b2")),
            mk(&missing, &d.join("m2")),
            mk(&c, &d.join("c2")),
        ];
        let e = apply(&p).unwrap_err();
        assert!(e.contains("rolled back"), "{e}");
        for n in ["a", "b", "c"] {
            assert_eq!(fs::read_to_string(dir.path().join(n)).unwrap(), n);
        }
        assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 3);
    }

    #[test]
    fn apply_never_overwrites_and_rolls_back() {
        let dir = tempdir().unwrap();
        let a = touch(dir.path(), "a");
        let b = touch(dir.path(), "b");
        let z = touch(dir.path(), "z");
        let e = apply(&[mk(&a, &dir.path().join("a2")), mk(&b, &z)]).unwrap_err();
        assert!(e.contains("already exists"), "{e}");
        assert_eq!(fs::read_to_string(&a).unwrap(), "a");
        assert_eq!(fs::read_to_string(&b).unwrap(), "b");
        assert_eq!(fs::read_to_string(&z).unwrap(), "z");
    }

    #[test]
    fn apply_refuses_previews_with_errors() {
        let dir = tempdir().unwrap();
        let files = [touch(dir.path(), "a.txt"), touch(dir.path(), "b.txt")];
        let bad = preview(&files, &rule("same", "[E]"));
        assert!(apply(&bad).is_err());
        assert!(files.iter().all(|f| f.exists()));
    }

    #[test]
    fn renames_directories_too() {
        let dir = tempdir().unwrap();
        let d = dir.path().join("old");
        fs::create_dir(&d).unwrap();
        fs::write(d.join("inner"), "").unwrap();
        let p = preview(std::slice::from_ref(&d), &sr("old", "new", false));
        apply(&p).unwrap();
        assert!(dir.path().join("new").join("inner").exists());
    }
}
