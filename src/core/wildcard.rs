//! File-name masks: `*.txt;*.md`, `a?.log`, `*.rs | test_*` (includes `|` excludes).
//!
//! Matching is case-insensitive. A mask without `*` or `?` matches the whole name exactly.

/// One glob (already lower-cased): `*` = any run of characters, `?` = exactly one.
#[derive(Clone, Debug)]
struct Glob(Vec<char>);

impl Glob {
    fn new(pattern: &str) -> Glob {
        Glob(pattern.to_lowercase().chars().collect())
    }

    /// `*` and `*.*` both mean "every name" (even one without a dot).
    fn is_everything(&self) -> bool {
        let p: String = self.0.iter().collect();
        p == "*" || p == "*.*"
    }

    /// Iterative matcher with single-star backtracking: O(name * pattern) worst case.
    fn matches(&self, name: &[char]) -> bool {
        let pat = &self.0;
        let (mut p, mut n) = (0, 0);
        let mut star: Option<(usize, usize)> = None;
        while n < name.len() {
            if p < pat.len() && pat[p] == '*' {
                star = Some((p, n));
                p += 1;
            } else if p < pat.len() && (pat[p] == '?' || pat[p] == name[n]) {
                p += 1;
                n += 1;
            } else if let Some((sp, sn)) = star {
                p = sp + 1;
                n = sn + 1;
                star = Some((sp, sn + 1));
            } else {
                return false;
            }
        }
        while p < pat.len() && pat[p] == '*' {
            p += 1;
        }
        p == pat.len()
    }
}

/// A parsed mask list with optional exclusions.
#[derive(Clone, Debug)]
pub struct Mask {
    includes: Vec<Glob>,
    excludes: Vec<Glob>,
    match_all: bool,
}

/// Splits one side of the `|` into globs: on `;` when present (names may then hold spaces),
/// else on whitespace.
fn split_globs(part: &str) -> Vec<Glob> {
    let pieces: Vec<&str> = if part.contains(';') {
        part.split(';').collect()
    } else {
        part.split_whitespace().collect()
    };
    pieces
        .into_iter()
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .map(Glob::new)
        .collect()
}

impl Mask {
    /// Parses `"*.txt;*.md"`, `"*.txt *.md"` or `"a.txt | *.bak"`. An empty include list
    /// (`""`, `"| *.bak"`) matches everything.
    pub fn new(pattern: &str) -> Mask {
        let (inc, exc) = match pattern.split_once('|') {
            Some((i, e)) => (i, e),
            None => (pattern, ""),
        };
        let includes = split_globs(inc);
        let excludes = split_globs(exc);
        let match_all = includes.is_empty() || includes.iter().any(Glob::is_everything);
        Mask {
            includes,
            excludes,
            match_all,
        }
    }

    /// True when `name` matches an include and no exclude.
    pub fn matches(&self, name: &str) -> bool {
        let chars: Vec<char> = name.to_lowercase().chars().collect();
        if self.excludes.iter().any(|g| g.matches(&chars)) {
            return false;
        }
        self.match_all || self.includes.iter().any(|g| g.matches(&chars))
    }

    /// True when the mask accepts every name (no exclusions, an everything include or none).
    pub fn is_match_all(&self) -> bool {
        self.match_all && self.excludes.is_empty()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn include_star_extension() {
        let m = Mask::new("*.txt");
        assert!(m.matches("a.txt"));
        assert!(m.matches(".txt"));
        assert!(!m.matches("a.txt.bak"));
        assert!(!m.matches("a.md"));
    }

    #[test]
    fn multi_mask_semicolon_and_space() {
        for p in ["*.txt;*.md", "*.txt *.md", "*.txt; *.md"] {
            let m = Mask::new(p);
            assert!(m.matches("a.txt"), "{p}");
            assert!(m.matches("b.md"), "{p}");
            assert!(!m.matches("c.rs"), "{p}");
        }
    }

    #[test]
    fn semicolon_keeps_spaces_in_names() {
        let m = Mask::new("my file.txt;*.md");
        assert!(m.matches("my file.txt"));
        assert!(!m.matches("my"));
    }

    #[test]
    fn exclude_after_pipe() {
        let m = Mask::new("*.rs | test_*;*_old.rs");
        assert!(m.matches("main.rs"));
        assert!(!m.matches("test_a.rs"));
        assert!(!m.matches("lib_old.rs"));
        assert!(!m.matches("main.txt"));
    }

    #[test]
    fn exclude_only_means_everything_else() {
        let m = Mask::new("| *.bak");
        assert!(m.matches("a.txt"));
        assert!(!m.matches("a.bak"));
        assert!(!m.is_match_all());
    }

    #[test]
    fn question_mark_is_one_char() {
        let m = Mask::new("a?c.txt");
        assert!(m.matches("abc.txt"));
        assert!(!m.matches("ac.txt"));
        assert!(!m.matches("abbc.txt"));
    }

    #[test]
    fn case_insensitive() {
        assert!(Mask::new("*.TXT").matches("readme.txt"));
        assert!(Mask::new("README.md").matches("readme.MD"));
    }

    #[test]
    fn without_wildcard_is_exact_not_substring() {
        let m = Mask::new("a.txt");
        assert!(m.matches("a.txt"));
        assert!(m.matches("A.TXT"));
        assert!(!m.matches("ba.txt"));
        assert!(!m.matches("a.txt2"));
        assert!(!Mask::new("note").matches("notes.txt"));
    }

    #[test]
    fn match_all_variants() {
        for p in ["", "*", "*.*", "  ", "a.txt;*"] {
            let m = Mask::new(p);
            assert!(m.is_match_all(), "{p:?}");
            assert!(m.matches("noext"), "{p:?}");
            assert!(m.matches("x.y"), "{p:?}");
        }
        assert!(!Mask::new("*.txt").is_match_all());
        assert!(!Mask::new("* | *.bak").is_match_all());
    }

    #[test]
    fn star_backtracking_and_unicode() {
        assert!(Mask::new("*a*b*c").matches("xxaxxbxxc"));
        assert!(!Mask::new("*a*b*c").matches("xxaxxbxx"));
        assert!(Mask::new("*.tar.gz").matches("a.tar.gz"));
        assert!(Mask::new("é?.txt").matches("ÉA.txt"));
    }
}
