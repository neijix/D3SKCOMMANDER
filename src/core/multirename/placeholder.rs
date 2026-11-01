//! Mask expansion: literal text, `[[` and the `[...]` placeholders (name, extension, parent
//! folders, modification date, character ranges, counter).

use std::fs;
use std::path::Path;
use std::time::SystemTime;

use chrono::{DateTime, Local};

use super::RenameRule;

/// Everything a placeholder may read about the file being renamed.
pub(super) struct Ctx<'a> {
    pub(super) file: &'a Path,
    pub(super) index: usize,
    pub(super) rule: &'a RenameRule,
    pub(super) stem: String,
    pub(super) ext: String,
}

impl Ctx<'_> {
    fn mtime(&self) -> Result<DateTime<Local>, String> {
        let modified: SystemTime =
            fs::metadata(self.file)
                .and_then(|m| m.modified())
                .map_err(|e| {
                    format!(
                        "cannot read the modification time of {}: {e}",
                        self.file.display()
                    )
                })?;
        Ok(DateTime::from(modified))
    }

    fn date(&self, fmt: &str) -> Result<String, String> {
        Ok(self.mtime()?.format(fmt).to_string())
    }

    fn ancestor_name(&self, level: usize) -> String {
        self.file
            .ancestors()
            .nth(level)
            .and_then(Path::file_name)
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default()
    }

    fn token(&self, tok: &str) -> Result<String, String> {
        match tok {
            "N" => Ok(self.stem.clone()),
            "E" => Ok(self.ext.clone()),
            "P" => Ok(self.ancestor_name(1)),
            "G" => Ok(self.ancestor_name(2)),
            _ => match date_format(tok) {
                Some(fmt) => self.date(fmt),
                None => self.parametric(tok),
            },
        }
    }

    /// `[N<range>]`, `[E<range>]` and `[C...]`: a letter followed by its parameters.
    fn parametric(&self, tok: &str) -> Result<String, String> {
        match tok.split_at_checked(1) {
            Some(("N", spec)) => char_range(&self.stem, spec, tok),
            Some(("E", spec)) => char_range(&self.ext, spec, tok),
            Some(("C", spec)) => self.counter(spec, tok),
            _ => Err(format!("unknown placeholder [{tok}]")),
        }
    }

    /// `[C]` or the inline form `[C<start>+<step>:<digits>]`, each part optional.
    fn counter(&self, spec: &str, tok: &str) -> Result<String, String> {
        let bad = || format!("invalid counter placeholder [{tok}]");
        let (rest, digits) = match spec.split_once(':') {
            Some((r, d)) => (r, Some(d)),
            None => (spec, None),
        };
        let (start, step) = match rest.split_once('+') {
            Some((s, t)) => (s, Some(t)),
            None => (rest, None),
        };
        let start = parse_part(start, self.rule.counter_start).ok_or_else(bad)?;
        let step = match step {
            Some(t) => parse_part(t, self.rule.counter_step).ok_or_else(bad)?,
            None => self.rule.counter_step,
        };
        let width = match digits {
            Some(d) => parse_part(d, self.rule.counter_digits).ok_or_else(bad)?,
            None => self.rule.counter_digits,
        };
        let value = start.saturating_add(step.saturating_mul(self.index as i64));
        Ok(format!("{value:0width$}"))
    }
}

/// strftime pattern of the modification-date placeholders.
fn date_format(tok: &str) -> Option<&'static str> {
    Some(match tok {
        "Y" => "%Y",
        "M" => "%m",
        "D" => "%d",
        "h" => "%H",
        "m" => "%M",
        "s" => "%S",
        "YMD" => "%Y%m%d",
        "hms" => "%H%M%S",
        _ => return None,
    })
}

/// Empty text keeps the default; otherwise the text must parse.
fn parse_part<T: std::str::FromStr>(text: &str, default: T) -> Option<T> {
    if text.is_empty() {
        Some(default)
    } else {
        text.parse().ok()
    }
}

/// `x`, `x-y` or `x-` (1-based, inclusive, counted in chars) of `text`.
fn char_range(text: &str, spec: &str, tok: &str) -> Result<String, String> {
    if spec.is_empty() {
        return Ok(text.to_string());
    }
    let bad = || format!("invalid range in placeholder [{tok}]");
    let (first, last) = match spec.split_once('-') {
        Some((a, b)) => (a, Some(b)),
        None => (spec, None),
    };
    let first: usize = first.parse().map_err(|_| bad())?;
    if first == 0 {
        return Err(bad());
    }
    let count = match last {
        None => 1,
        Some("") => usize::MAX,
        Some(b) => {
            let last: usize = b.parse().map_err(|_| bad())?;
            if last < first {
                return Err(bad());
            }
            last - first + 1
        }
    };
    Ok(text.chars().skip(first - 1).take(count).collect())
}

/// Expand one mask: literal text, `[[` for a literal `[`, `[...]` placeholders.
pub(super) fn expand(mask: &str, ctx: &Ctx) -> Result<String, String> {
    let mut out = String::new();
    let mut rest = mask;
    while let Some(pos) = rest.find('[') {
        out.push_str(&rest[..pos]);
        rest = &rest[pos + 1..];
        if let Some(after) = rest.strip_prefix('[') {
            out.push('[');
            rest = after;
            continue;
        }
        let end = rest
            .find(']')
            .ok_or_else(|| format!("unterminated placeholder in \"{mask}\""))?;
        out.push_str(&ctx.token(&rest[..end])?);
        rest = &rest[end + 1..];
    }
    out.push_str(rest);
    Ok(out)
}
