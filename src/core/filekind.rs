//! What a panel entry is, for its icon and colour: folder, archive, image, code …
//! Decided from the entry flags and the file extension only (no file is opened).

use std::path::Path;

use super::archive::ArchiveKind;
use super::fs::Entry;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FileKind {
    Parent,
    Folder,
    Link,
    Archive,
    Image,
    Audio,
    Video,
    Pdf,
    Document,
    Spreadsheet,
    Code,
    Executable,
    Text,
    Other,
}

const IMAGE: &[&str] = &[
    "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "tif", "tiff", "svg", "heic", "avif", "psd",
    "raw", "cr2", "nef",
];
const AUDIO: &[&str] = &[
    "mp3", "wav", "flac", "ogg", "oga", "opus", "m4a", "aac", "wma", "aiff", "mid",
];
const VIDEO: &[&str] = &[
    "mp4", "mkv", "avi", "mov", "webm", "wmv", "flv", "m4v", "mpg", "mpeg", "3gp",
];
const DOCUMENT: &[&str] = &[
    "doc", "docx", "odt", "rtf", "pages", "ppt", "pptx", "odp", "key", "epub",
];
const SPREADSHEET: &[&str] = &["xls", "xlsx", "ods", "csv", "tsv", "numbers", "dbf"];
const CODE: &[&str] = &[
    "rs", "c", "h", "cpp", "hpp", "cc", "cs", "java", "kt", "go", "py", "rb", "php", "js", "mjs",
    "ts", "tsx", "jsx", "html", "htm", "css", "scss", "json", "toml", "yaml", "yml", "xml", "sql",
    "swift", "lua", "sh", "bash", "zsh", "ps1", "bat", "cmd", "vue", "svelte",
];
const EXECUTABLE: &[&str] = &[
    "exe", "msi", "com", "app", "appimage", "deb", "rpm", "dmg", "pkg", "apk", "jar",
];
const TEXT: &[&str] = &["txt", "md", "log", "ini", "cfg", "conf", "nfo", "readme"];

impl FileKind {
    pub fn of(e: &Entry) -> FileKind {
        if e.is_parent {
            FileKind::Parent
        } else if e.is_dir {
            FileKind::Folder
        } else if e.is_link {
            FileKind::Link
        } else {
            Self::of_file(&e.path)
        }
    }

    /// The kind of a regular file, from its name.
    pub fn of_file(path: &Path) -> FileKind {
        if ArchiveKind::from_path(path).is_some() {
            return FileKind::Archive;
        }
        let ext = path
            .extension()
            .map(|x| x.to_string_lossy().to_lowercase())
            .unwrap_or_default();
        let ext = ext.as_str();
        let table: [(&[&str], FileKind); 9] = [
            (IMAGE, FileKind::Image),
            (AUDIO, FileKind::Audio),
            (VIDEO, FileKind::Video),
            (&["pdf"], FileKind::Pdf),
            (DOCUMENT, FileKind::Document),
            (SPREADSHEET, FileKind::Spreadsheet),
            (CODE, FileKind::Code),
            (EXECUTABLE, FileKind::Executable),
            (TEXT, FileKind::Text),
        ];
        table
            .iter()
            .find(|(exts, _)| exts.contains(&ext))
            .map_or(FileKind::Other, |(_, k)| *k)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn file(name: &str) -> FileKind {
        FileKind::of_file(&PathBuf::from(name))
    }

    #[test]
    fn kinds_follow_the_extension_case_insensitively() {
        assert_eq!(file("a.PNG"), FileKind::Image);
        assert_eq!(file("song.flac"), FileKind::Audio);
        assert_eq!(file("clip.mkv"), FileKind::Video);
        assert_eq!(file("report.pdf"), FileKind::Pdf);
        assert_eq!(file("letter.docx"), FileKind::Document);
        assert_eq!(file("table.csv"), FileKind::Spreadsheet);
        assert_eq!(file("main.rs"), FileKind::Code);
        assert_eq!(file("setup.exe"), FileKind::Executable);
        assert_eq!(file("notes.md"), FileKind::Text);
        assert_eq!(file("Makefile"), FileKind::Other);
        assert_eq!(file(".bashrc"), FileKind::Other);
    }

    #[test]
    fn archives_win_over_other_kinds() {
        assert_eq!(file("src.tar.gz"), FileKind::Archive);
        assert_eq!(file("a.zip"), FileKind::Archive);
        assert_eq!(file("b.7z"), FileKind::Archive);
    }

    #[test]
    fn entry_flags_come_first() {
        let mut e = crate::core::fs::entry_from_path(&std::env::temp_dir()).unwrap();
        assert_eq!(FileKind::of(&e), FileKind::Folder);
        e.is_dir = false;
        e.is_link = true;
        assert_eq!(FileKind::of(&e), FileKind::Link);
        e.is_parent = true;
        assert_eq!(FileKind::of(&e), FileKind::Parent);
    }
}
