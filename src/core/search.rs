//! File search: by name (mask or regex), content, size and date, over several roots.

use std::fs::{File, Metadata};
use std::io::{self, BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc;
use std::time::SystemTime;

use regex::bytes::{Regex, RegexBuilder};
use walkdir::WalkDir;

use super::wildcard::Mask;

/// Files above this size are not searched for content.
const MAX_CONTENT_SIZE: u64 = 512 * 1024 * 1024;
/// A NUL byte in this prefix marks a file as binary.
const BINARY_PROBE: usize = 8 * 1024;
/// Longest matching line reported, in characters.
const MAX_LINE_CHARS: usize = 300;
/// A `Scanned` event is sent every this many entries.
const SCAN_EVERY: u64 = 64;

/// What to look for. `max_depth` counts levels below a root: `Some(1)` = direct children only.
#[derive(Clone, Debug)]
pub struct SearchQuery {
    pub roots: Vec<PathBuf>,
    /// Mask (see `wildcard`) or, with `name_is_regex`, a regex searched in the file name.
    pub name: String,
    pub name_is_regex: bool,
    /// Text or regex looked for inside files; an empty string counts as `None`.
    pub content: Option<String>,
    pub content_is_regex: bool,
    /// Applies to the content (and to a regex name); a mask name is always case-insensitive.
    pub case_sensitive: bool,
    pub whole_words: bool,
    /// Keep the text files that do NOT contain `content`.
    pub not_containing: bool,
    pub max_depth: Option<usize>,
    pub include_hidden: bool,
    /// Size bounds apply to files only (a directory has no meaningful size).
    pub min_size: Option<u64>,
    pub max_size: Option<u64>,
    pub newer_than: Option<SystemTime>,
    pub older_than: Option<SystemTime>,
    pub files: bool,
    pub dirs: bool,
    pub follow_links: bool,
}

impl Default for SearchQuery {
    fn default() -> Self {
        SearchQuery {
            roots: Vec::new(),
            name: "*".to_string(),
            name_is_regex: false,
            content: None,
            content_is_regex: false,
            case_sensitive: false,
            whole_words: false,
            not_containing: false,
            max_depth: None,
            include_hidden: false,
            min_size: None,
            max_size: None,
            newer_than: None,
            older_than: None,
            files: true,
            dirs: true,
            follow_links: false,
        }
    }
}

/// One matching entry; `line` is the first matching line (1-based) of a content search.
#[derive(Clone, Debug)]
pub struct SearchHit {
    pub path: PathBuf,
    pub is_dir: bool,
    pub size: u64,
    pub modified: Option<SystemTime>,
    pub line: Option<(usize, String)>,
}

#[derive(Clone, Debug)]
pub enum SearchEvent {
    Hit(SearchHit),
    Scanned { count: u64, current: PathBuf },
    Done { hits: u64, cancelled: bool },
    Error(String),
}

/// True for a dot-file (every OS) or, on Windows, a file with the hidden attribute.
pub fn is_hidden(path: &Path, meta: &Metadata) -> bool {
    let dotted = path
        .file_name()
        .is_some_and(|n| n.to_string_lossy().starts_with('.'));
    dotted || has_hidden_attribute(meta)
}

#[cfg(windows)]
fn has_hidden_attribute(meta: &Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    const FILE_ATTRIBUTE_HIDDEN: u32 = 0x2;
    meta.file_attributes() & FILE_ATTRIBUTE_HIDDEN != 0
}

#[cfg(not(windows))]
fn has_hidden_attribute(_meta: &Metadata) -> bool {
    false
}

enum NameMatcher {
    Mask(Mask),
    Regex(Regex),
}

impl NameMatcher {
    fn build(q: &SearchQuery) -> Result<NameMatcher, String> {
        if q.name_is_regex {
            let re = RegexBuilder::new(&q.name)
                .case_insensitive(!q.case_sensitive)
                .build()
                .map_err(|e| format!("invalid name regex: {e}"))?;
            Ok(NameMatcher::Regex(re))
        } else {
            Ok(NameMatcher::Mask(Mask::new(&q.name)))
        }
    }

    fn matches(&self, name: &str) -> bool {
        match self {
            NameMatcher::Mask(m) => m.matches(name),
            NameMatcher::Regex(re) => re.is_match(name.as_bytes()),
        }
    }
}

/// Outcome of looking for the content pattern in one file.
enum Scan {
    Found(usize, String),
    NotFound,
    /// Binary (for a regex), too large, unreadable or cancelled: neither found nor "not containing".
    Skipped,
}

struct ContentMatcher {
    re: Regex,
    /// Plain text is searched even in binary files (byte search).
    plain: bool,
}

impl ContentMatcher {
    fn build(q: &SearchQuery, text: &str) -> Result<ContentMatcher, String> {
        let mut pat = if q.content_is_regex {
            text.to_string()
        } else {
            regex::escape(text)
        };
        if q.whole_words {
            pat = format!(r"\b(?:{pat})\b");
        }
        let re = RegexBuilder::new(&pat)
            .case_insensitive(!q.case_sensitive)
            .build()
            .map_err(|e| format!("invalid content pattern: {e}"))?;
        Ok(ContentMatcher {
            re,
            plain: !q.content_is_regex,
        })
    }

    fn scan(&self, path: &Path, size: u64, cancel: &AtomicBool) -> io::Result<Scan> {
        if size > MAX_CONTENT_SIZE {
            return Ok(Scan::Skipped);
        }
        let mut reader = BufReader::with_capacity(64 * 1024, File::open(path)?);
        let head = reader.fill_buf()?;
        let probe = &head[..head.len().min(BINARY_PROBE)];
        if !self.plain && probe.contains(&0) {
            return Ok(Scan::Skipped);
        }
        let mut buf = Vec::new();
        let mut line_no = 0usize;
        loop {
            if cancel.load(Ordering::Relaxed) {
                return Ok(Scan::Skipped);
            }
            buf.clear();
            if reader.read_until(b'\n', &mut buf)? == 0 {
                return Ok(Scan::NotFound);
            }
            line_no += 1;
            if self.re.is_match(&buf) {
                return Ok(Scan::Found(line_no, display_line(&buf)));
            }
        }
    }
}

fn display_line(bytes: &[u8]) -> String {
    String::from_utf8_lossy(bytes)
        .trim_end_matches(['\r', '\n'])
        .chars()
        .take(MAX_LINE_CHARS)
        .collect()
}

/// Everything compiled once per search.
struct Plan<'a> {
    q: &'a SearchQuery,
    name: NameMatcher,
    content: Option<ContentMatcher>,
}

impl Plan<'_> {
    fn build(q: &SearchQuery) -> Result<Plan<'_>, String> {
        let content = match q.content.as_deref().filter(|c| !c.is_empty()) {
            Some(text) => Some(ContentMatcher::build(q, text)?),
            None => None,
        };
        Ok(Plan {
            q,
            name: NameMatcher::build(q)?,
            content,
        })
    }

    /// Metadata filters shared by files and directories.
    fn passes_meta(&self, meta: &Metadata, is_dir: bool) -> bool {
        let q = self.q;
        if !is_dir
            && (q.min_size.is_some_and(|min| meta.len() < min)
                || q.max_size.is_some_and(|max| meta.len() > max))
        {
            return false;
        }
        if q.newer_than.is_some() || q.older_than.is_some() {
            let Ok(modified) = meta.modified() else {
                return false;
            };
            if q.newer_than.is_some_and(|t| modified <= t)
                || q.older_than.is_some_and(|t| modified >= t)
            {
                return false;
            }
        }
        true
    }

    /// Evaluates one entry; `Ok(None)` = no hit. Content errors are returned for reporting.
    fn evaluate(
        &self,
        path: &Path,
        meta: &Metadata,
        cancel: &AtomicBool,
    ) -> Result<Option<SearchHit>, String> {
        let is_dir = meta.is_dir();
        if (is_dir && !self.q.dirs) || (!is_dir && !self.q.files) {
            return Ok(None);
        }
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        if !self.name.matches(&name) || !self.passes_meta(meta, is_dir) {
            return Ok(None);
        }
        let mut line = None;
        if let Some(cm) = &self.content {
            if !meta.is_file() {
                return Ok(None);
            }
            let scan = cm
                .scan(path, meta.len(), cancel)
                .map_err(|e| format!("{}: {e}", path.display()))?;
            match (scan, self.q.not_containing) {
                (Scan::Found(n, text), false) => line = Some((n, text)),
                (Scan::NotFound, true) => {}
                _ => return Ok(None),
            }
        }
        Ok(Some(SearchHit {
            path: path.to_path_buf(),
            is_dir,
            size: if is_dir { 0 } else { meta.len() },
            modified: meta.modified().ok(),
            line,
        }))
    }
}

/// Runs the search synchronously, reporting through `on_event`; always ends with `Done`.
pub fn search(q: &SearchQuery, cancel: &AtomicBool, on_event: &mut dyn FnMut(SearchEvent)) {
    let plan = match Plan::build(q) {
        Ok(p) => p,
        Err(msg) => {
            on_event(SearchEvent::Error(msg));
            on_event(SearchEvent::Done {
                hits: 0,
                cancelled: false,
            });
            return;
        }
    };
    let (mut hits, mut count) = (0u64, 0u64);
    let mut cancelled = false;
    'roots: for root in &q.roots {
        let mut walker = WalkDir::new(root).follow_links(q.follow_links);
        if let Some(d) = q.max_depth {
            walker = walker.max_depth(d);
        }
        let include_hidden = q.include_hidden;
        let entries = walker.into_iter().filter_entry(|e| {
            if include_hidden || e.depth() == 0 {
                return true;
            }
            e.metadata().map_or(true, |m| !is_hidden(e.path(), &m))
        });
        for entry in entries {
            if cancel.load(Ordering::Relaxed) {
                cancelled = true;
                break 'roots;
            }
            let entry = match entry {
                Ok(e) => e,
                Err(e) => {
                    on_event(SearchEvent::Error(e.to_string()));
                    continue;
                }
            };
            count += 1;
            if count % SCAN_EVERY == 0 {
                on_event(SearchEvent::Scanned {
                    count,
                    current: entry.path().to_path_buf(),
                });
            }
            // The root directory itself is the starting point, not a result.
            if entry.depth() == 0 && entry.file_type().is_dir() {
                continue;
            }
            let meta = match entry.metadata() {
                Ok(m) => m,
                Err(e) => {
                    on_event(SearchEvent::Error(e.to_string()));
                    continue;
                }
            };
            match plan.evaluate(entry.path(), &meta, cancel) {
                Ok(Some(hit)) => {
                    hits += 1;
                    on_event(SearchEvent::Hit(hit));
                }
                Ok(None) => {}
                Err(msg) => on_event(SearchEvent::Error(msg)),
            }
        }
    }
    // A cancel raised during a content scan surfaces here.
    cancelled |= cancel.load(Ordering::Relaxed);
    on_event(SearchEvent::Done { hits, cancelled });
}

/// Runs `search` on a background thread; the receiver closing cancels it.
pub fn spawn(q: SearchQuery, cancel: Arc<AtomicBool>) -> mpsc::Receiver<SearchEvent> {
    let (tx, rx) = mpsc::channel();
    std::thread::spawn(move || {
        let flag = Arc::clone(&cancel);
        search(&q, &cancel, &mut |ev| {
            if tx.send(ev).is_err() {
                flag.store(true, Ordering::Relaxed);
            }
        });
    });
    rx
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::time::Duration;

    fn write(dir: &Path, rel: &str, data: &[u8]) -> PathBuf {
        let p = dir.join(rel);
        if let Some(parent) = p.parent() {
            fs::create_dir_all(parent).unwrap();
        }
        fs::write(&p, data).unwrap();
        p
    }

    fn run(q: &SearchQuery) -> (Vec<SearchHit>, Vec<String>, bool) {
        let cancel = AtomicBool::new(false);
        run_with(q, &cancel)
    }

    fn run_with(q: &SearchQuery, cancel: &AtomicBool) -> (Vec<SearchHit>, Vec<String>, bool) {
        let (mut hits, mut errors, mut was_cancelled) = (Vec::new(), Vec::new(), false);
        let mut done = false;
        search(q, cancel, &mut |ev| match ev {
            SearchEvent::Hit(h) => hits.push(h),
            SearchEvent::Error(e) => errors.push(e),
            SearchEvent::Done { hits: n, cancelled } => {
                assert_eq!(n as usize, hits.len());
                was_cancelled = cancelled;
                done = true;
            }
            SearchEvent::Scanned { .. } => {}
        });
        assert!(done, "Done must always be sent");
        (hits, errors, was_cancelled)
    }

    fn names(hits: &[SearchHit]) -> Vec<String> {
        let mut v: Vec<String> = hits
            .iter()
            .map(|h| h.path.file_name().unwrap().to_string_lossy().into_owned())
            .collect();
        v.sort();
        v
    }

    fn query(root: &Path) -> SearchQuery {
        SearchQuery {
            roots: vec![root.to_path_buf()],
            ..SearchQuery::default()
        }
    }

    #[test]
    fn by_name_mask_with_exclude() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "a.txt", b"x");
        write(d.path(), "sub/b.TXT", b"x");
        write(d.path(), "sub/skip_c.txt", b"x");
        write(d.path(), "d.md", b"x");
        let mut q = query(d.path());
        q.name = "*.txt | skip_*".into();
        let (hits, errors, cancelled) = run(&q);
        assert_eq!(names(&hits), ["a.txt", "b.TXT"]);
        assert!(errors.is_empty() && !cancelled);
    }

    #[test]
    fn by_name_regex() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "file1.log", b"x");
        write(d.path(), "file22.log", b"x");
        write(d.path(), "fileX.log", b"x");
        let mut q = query(d.path());
        q.name = r"^file\d+\.log$".into();
        q.name_is_regex = true;
        assert_eq!(names(&run(&q).0), ["file1.log", "file22.log"]);
    }

    #[test]
    fn invalid_regex_reports_error_then_done() {
        let d = tempfile::tempdir().unwrap();
        let mut q = query(d.path());
        q.name = "(".into();
        q.name_is_regex = true;
        let (hits, errors, _) = run(&q);
        assert!(hits.is_empty());
        assert_eq!(errors.len(), 1);
    }

    #[test]
    fn content_search_reports_line_number() {
        let d = tempfile::tempdir().unwrap();
        write(
            d.path(),
            "a.txt",
            b"one\ntwo\r\nNeedle here\nNeedle again\n",
        );
        write(d.path(), "b.txt", b"nothing");
        let mut q = query(d.path());
        q.content = Some("needle".into());
        let (hits, _, _) = run(&q);
        assert_eq!(names(&hits), ["a.txt"]);
        assert_eq!(hits[0].line, Some((3, "Needle here".to_string())));
        q.case_sensitive = true;
        assert!(run(&q).0.is_empty());
    }

    #[test]
    fn content_regex_and_whole_words() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "a.txt", b"foobar\n");
        write(d.path(), "b.txt", b"a foo b\n");
        let mut q = query(d.path());
        q.content = Some("foo".into());
        q.whole_words = true;
        assert_eq!(names(&run(&q).0), ["b.txt"]);
        q.whole_words = false;
        q.content = Some(r"fo+\s?b".into());
        q.content_is_regex = true;
        assert_eq!(names(&run(&q).0), ["a.txt", "b.txt"]);
        q.content = Some("[".into());
        let (hits, errors, _) = run(&q);
        assert!(hits.is_empty() && errors.len() == 1);
    }

    #[test]
    fn content_skips_dirs_and_binary_for_regex_only() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "bin.dat", b"abc\0needle\n");
        fs::create_dir(d.path().join("needle_dir")).unwrap();
        let mut q = query(d.path());
        q.content = Some("needle".into());
        // plain text: byte search finds it in the binary file; directories never match
        assert_eq!(names(&run(&q).0), ["bin.dat"]);
        q.content_is_regex = true;
        assert!(run(&q).0.is_empty());
    }

    #[test]
    fn not_containing_lists_files_without_the_text() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "has.txt", b"secret\n");
        write(d.path(), "no.txt", b"public\n");
        let mut q = query(d.path());
        q.content = Some("secret".into());
        q.not_containing = true;
        let (hits, _, _) = run(&q);
        assert_eq!(names(&hits), ["no.txt"]);
        assert!(hits[0].line.is_none());
    }

    #[test]
    fn size_filters() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "s.bin", &[0; 10]);
        write(d.path(), "m.bin", &[0; 100]);
        write(d.path(), "l.bin", &[0; 1000]);
        let mut q = query(d.path());
        q.min_size = Some(50);
        q.max_size = Some(500);
        assert_eq!(names(&run(&q).0), ["m.bin"]);
        q.min_size = None;
        assert_eq!(names(&run(&q).0), ["m.bin", "s.bin"]);
    }

    #[test]
    fn date_filters() {
        let d = tempfile::tempdir().unwrap();
        let old = write(d.path(), "old.txt", b"x");
        let new = write(d.path(), "new.txt", b"x");
        let base = SystemTime::UNIX_EPOCH + Duration::from_secs(1_700_000_000);
        filetime::set_file_mtime(&old, filetime::FileTime::from_system_time(base)).unwrap();
        let later = base + Duration::from_secs(10_000);
        filetime::set_file_mtime(&new, filetime::FileTime::from_system_time(later)).unwrap();
        let mid = base + Duration::from_secs(5_000);
        let mut q = query(d.path());
        q.newer_than = Some(mid);
        assert_eq!(names(&run(&q).0), ["new.txt"]);
        q.newer_than = None;
        q.older_than = Some(mid);
        assert_eq!(names(&run(&q).0), ["old.txt"]);
        q.newer_than = Some(base + Duration::from_secs(20_000));
        assert!(run(&q).0.is_empty());
    }

    #[test]
    fn max_depth_limits_levels_below_root() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "top.txt", b"x");
        write(d.path(), "a/mid.txt", b"x");
        write(d.path(), "a/b/deep.txt", b"x");
        let mut q = query(d.path());
        q.files = true;
        q.dirs = false;
        q.max_depth = Some(1);
        assert_eq!(names(&run(&q).0), ["top.txt"]);
        q.max_depth = Some(2);
        assert_eq!(names(&run(&q).0), ["mid.txt", "top.txt"]);
        q.max_depth = None;
        assert_eq!(names(&run(&q).0), ["deep.txt", "mid.txt", "top.txt"]);
    }

    #[test]
    fn hidden_filter() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "vis.txt", b"x");
        write(d.path(), ".hid.txt", b"x");
        write(d.path(), ".hdir/inner.txt", b"x");
        let mut q = query(d.path());
        q.dirs = false;
        assert_eq!(names(&run(&q).0), ["vis.txt"]);
        q.include_hidden = true;
        assert_eq!(names(&run(&q).0), [".hid.txt", "inner.txt", "vis.txt"]);
    }

    #[test]
    fn is_hidden_detects_dot_files() {
        let d = tempfile::tempdir().unwrap();
        let dot = write(d.path(), ".x", b"");
        let plain = write(d.path(), "x", b"");
        assert!(is_hidden(&dot, &fs::metadata(&dot).unwrap()));
        assert!(!is_hidden(&plain, &fs::metadata(&plain).unwrap()));
    }

    #[test]
    fn dirs_only_and_files_only() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "f.txt", b"x");
        fs::create_dir(d.path().join("folder")).unwrap();
        let mut q = query(d.path());
        q.files = false;
        let (hits, _, _) = run(&q);
        assert_eq!(names(&hits), ["folder"]);
        assert!(hits[0].is_dir);
        q.files = true;
        q.dirs = false;
        let (hits, _, _) = run(&q);
        assert_eq!(names(&hits), ["f.txt"]);
        assert!(!hits[0].is_dir && hits[0].size == 1);
    }

    #[test]
    fn several_roots_and_missing_root_reports_error() {
        let a = tempfile::tempdir().unwrap();
        let b = tempfile::tempdir().unwrap();
        write(a.path(), "a.txt", b"x");
        write(b.path(), "b.txt", b"x");
        let mut q = query(a.path());
        q.roots.push(b.path().to_path_buf());
        q.roots.push(b.path().join("missing"));
        let (hits, errors, _) = run(&q);
        assert_eq!(names(&hits), ["a.txt", "b.txt"]);
        assert_eq!(errors.len(), 1);
    }

    #[test]
    fn cancel_stops_the_search() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "a.txt", b"x");
        let cancel = AtomicBool::new(true);
        let (hits, _, cancelled) = run_with(&query(d.path()), &cancel);
        assert!(hits.is_empty() && cancelled);
    }

    #[test]
    fn spawn_streams_events_until_done() {
        let d = tempfile::tempdir().unwrap();
        write(d.path(), "a.txt", b"x");
        let rx = spawn(query(d.path()), Arc::new(AtomicBool::new(false)));
        let events: Vec<SearchEvent> = rx.iter().collect();
        assert!(matches!(
            events.last(),
            Some(SearchEvent::Done {
                hits: 1,
                cancelled: false
            })
        ));
        assert!(events.iter().any(|e| matches!(e, SearchEvent::Hit(_))));
    }
}
