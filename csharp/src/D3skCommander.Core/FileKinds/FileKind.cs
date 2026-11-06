using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.FileKinds;

/// <summary>
/// What a panel entry is, for its icon and colour: folder, archive, image, code.
/// Decided from the entry flags and the file extension only (no file is opened).
/// </summary>
public enum FileKind
{
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

/// <summary>Decides a <see cref="FileKind"/> from entry flags, then the file name.</summary>
public static class FileKindClassifier
{
    // v0.1 ArchiveKind::from_path. ".jar" is also an executable extension; archives are checked first.
    private static readonly string[] TarGzSuffixes = [".tar.gz", ".tgz"];

    private static readonly string[] ZipSuffixes = [".zip", ".jar"];

    private static readonly string[] ExternalSuffixes =
    [
        ".rar", ".arj", ".lzh", ".lha", ".cab", ".ace", ".uc2", ".gz", ".bz2", ".xz", ".iso",
    ];

    private static readonly string[] Image =
    [
        "png", "jpg", "jpeg", "jpe", "jfif", "gif", "bmp", "dib", "webp", "ico", "tif", "tiff", "svg", "heic",
        "heif", "avif", "jxl", "jp2", "psd", "tga", "raw", "cr2", "cr3", "nef", "arw", "dng", "orf", "rw2",
        "raf", "pef", "srw",
    ];

    private static readonly string[] Audio =
    [
        "mp3", "wav", "flac", "ogg", "oga", "opus", "m4a", "aac", "wma", "aiff", "mid",
    ];

    private static readonly string[] Video =
    [
        "mp4", "mkv", "avi", "mov", "webm", "wmv", "flv", "m4v", "mpg", "mpeg", "3gp",
    ];

    private static readonly string[] Pdf = ["pdf"];

    private static readonly string[] Document =
    [
        "doc", "docx", "odt", "rtf", "pages", "ppt", "pptx", "odp", "key", "epub",
    ];

    private static readonly string[] Spreadsheet = ["xls", "xlsx", "ods", "csv", "tsv", "numbers", "dbf"];

    private static readonly string[] Code =
    [
        "rs", "c", "h", "cpp", "hpp", "cc", "cs", "java", "kt", "go", "py", "rb", "php", "js", "mjs",
        "ts", "tsx", "jsx", "html", "htm", "css", "scss", "json", "toml", "yaml", "yml", "xml", "sql",
        "swift", "lua", "sh", "bash", "zsh", "ps1", "bat", "cmd", "vue", "svelte",
    ];

    private static readonly string[] Executable =
    [
        "exe", "msi", "com", "app", "appimage", "deb", "rpm", "dmg", "pkg", "apk", "jar",
    ];

    private static readonly string[] Text = ["txt", "md", "log", "ini", "cfg", "conf", "nfo", "readme"];

    /// <summary>Flags first: parent, then folder, then link, then the file name.</summary>
    public static FileKind Of(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsParent)
        {
            return FileKind.Parent;
        }

        if (entry.IsDirectory)
        {
            return FileKind.Folder;
        }

        if (entry.IsLink)
        {
            return FileKind.Link;
        }

        return OfFile(entry.Path);
    }

    /// <summary>The kind of a regular file, from its name. Archives win over other extensions.</summary>
    public static FileKind OfFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path);
        if (IsArchiveName(name))
        {
            return FileKind.Archive;
        }

        var extension = LocalFileSystem.SplitExtension(name).Extension;
        if (extension.Length == 0)
        {
            return FileKind.Other;
        }

        return KindForExtension(extension);
    }

    private static bool IsArchiveName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        if (HasSuffix(name, TarGzSuffixes))
        {
            return true;
        }

        if (HasSuffix(name, ZipSuffixes))
        {
            return true;
        }

        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasSuffix(name, ExternalSuffixes);
    }

    private static FileKind KindForExtension(string extension)
    {
        if (Listed(Image, extension))
        {
            return FileKind.Image;
        }

        if (Listed(Audio, extension))
        {
            return FileKind.Audio;
        }

        if (Listed(Video, extension))
        {
            return FileKind.Video;
        }

        if (Listed(Pdf, extension))
        {
            return FileKind.Pdf;
        }

        if (Listed(Document, extension))
        {
            return FileKind.Document;
        }

        if (Listed(Spreadsheet, extension))
        {
            return FileKind.Spreadsheet;
        }

        if (Listed(Code, extension))
        {
            return FileKind.Code;
        }

        if (Listed(Executable, extension))
        {
            return FileKind.Executable;
        }

        if (Listed(Text, extension))
        {
            return FileKind.Text;
        }

        return FileKind.Other;
    }

    private static bool HasSuffix(string name, string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Listed(string[] extensions, string extension)
    {
        foreach (var candidate in extensions)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
