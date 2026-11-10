using Avalonia.Media;
using D3skCommander.Core.FileKinds;

namespace D3skCommander.App.Icons;

/// <summary>
/// The icon set: original geometry, one icon per <see cref="FileKind"/>. Mid-tone colours with a
/// white glyph stay readable on the light and the dark Fluent themes.
/// </summary>
public static class FileKindIcons
{
    private const string Page = "M3,1 L10,1 L13,4 L13,15 L3,15 Z";
    private const string Fold = "M10,1 L10,4 L13,4 Z";
    private const string Lines = "M4.8,6.5 h6.4 v1.2 h-6.4z M4.8,9 h6.4 v1.2 h-6.4z M4.8,11.5 h4 v1.2 h-4z";

    private static readonly Dictionary<FileKind, FileKindIcon> Table = Build();

    public static FileKindIcon For(FileKind kind) => Table[kind];

    public static IEnumerable<FileKindIcon> All => Table.Values;

    private static Dictionary<FileKind, FileKindIcon> Build()
    {
        var table = new Dictionary<FileKind, FileKindIcon>();
        Add(table, FileKind.Parent, "parent", "7C93B5", "M8,2 L14,8.5 L10,8.5 L10,14 L6,14 L6,8.5 L2,8.5 Z", null, null);
        Add(table, FileKind.Folder, "folder", "E8A317", "M1,3 L6,3 L7.5,4.5 L15,4.5 L15,13 L1,13 Z", "M1,6.5 L15,6.5 L15,13 L1,13 Z", null);
        Add(table, FileKind.Drive, "drive", "6E8496", "M1,3.5 h14 v9 h-14z", "M2.5,5 h11 v3.2 h-11z", "M10.6,9.6 h2.6 v1.5 h-2.6z");
        Add(table, FileKind.NetworkDrive, "network-drive", "3A86C8", PanelBarIcons.Network, null, null);
        Add(table, FileKind.Link, "link", "2BA4C4", Page, Fold, "M5.5,11 L9,7.5 L7.3,7.5 L7.3,6.5 L11.2,6.5 L11.2,10.4 L10.2,10.4 L10.2,8.7 L6.5,12 Z");
        Add(table, FileKind.Archive, "archive", "C07A2C", Page, Fold, "F1 M7.2,1.5 h1.6 v1.4 h-1.6z M7.2,4.3 h1.6 v1.4 h-1.6z M7.2,7.1 h1.6 v1.4 h-1.6z M6.4,9.6 h3.2 v3.8 h-3.2z M7.4,10.8 h1.2 v1.6 h-1.2z");
        Add(table, FileKind.Image, "image", "2FA97C", Page, Fold, "M4.5,13 L7,8.6 L8.8,11 L10,9.7 L11.5,13 Z M10.6,6.2 A1,1 0 1 1 10.6,8.2 A1,1 0 1 1 10.6,6.2 Z");
        Add(table, FileKind.Audio, "audio", "A064D6", Page, Fold, "M5,11.4 A1.6,1.3 0 1 1 8.2,11.4 A1.6,1.3 0 1 1 5,11.4 Z M7.6,11.4 L7.6,5.6 L11.2,6.6 L11.2,8.1 L8.6,7.4 L8.6,11.4 Z");
        Add(table, FileKind.Video, "video", "D6578F", "M1,3 L15,3 L15,13 L1,13 Z", "F1 M2,4 h1.6 v1.5 h-1.6z M5.6,4 h1.6 v1.5 h-1.6z M9,4 h1.6 v1.5 h-1.6z M12.4,4 h1.6 v1.5 h-1.6z M2,10.5 h1.6 v1.5 h-1.6z M5.6,10.5 h1.6 v1.5 h-1.6z M9,10.5 h1.6 v1.5 h-1.6z M12.4,10.5 h1.6 v1.5 h-1.6z", "M6.4,6 L10.8,8 L6.4,10 Z");
        Add(table, FileKind.Pdf, "pdf", "D64541", Page, Fold, "M4.8,6.5 h6.4 v1.2 h-6.4z M4.8,9 h6.4 v1.2 h-6.4z M4.8,11.5 h6.4 v1.2 h-6.4z");
        Add(table, FileKind.Document, "document", "3E8EDE", Page, Fold, Lines);
        Add(table, FileKind.Spreadsheet, "spreadsheet", "3B9E4A", Page, Fold, "M4.8,6 h6.4 v1.1 h-6.4z M4.8,8.5 h6.4 v1.1 h-6.4z M4.8,11 h6.4 v1.1 h-6.4z M7.5,6 h1.1 v6.1 h-1.1z");
        Add(table, FileKind.Code, "code", "5B6CD6", Page, Fold, "M6.2,6.4 L4,9.3 L6.2,12.2 L7,11.5 L5.4,9.3 L7,7.1 Z M9.8,6.4 L12,9.3 L9.8,12.2 L9,11.5 L10.6,9.3 L9,7.1 Z");
        Add(table, FileKind.Executable, "executable", "78909C", "M1,2.5 L15,2.5 L15,13.5 L1,13.5 Z", "M1,2.5 L15,2.5 L15,5 L1,5 Z", "M3.6,6.6 L4.4,6 L7.4,8.6 L4.4,11.2 L3.6,10.6 L5.9,8.6 Z M8.4,10.4 h3.8 v1.1 h-3.8z");
        Add(table, FileKind.Text, "text", "A5ADB8", Page, Fold, Lines);
        Add(table, FileKind.Other, "other", "B4BAC2", Page, Fold, null);
        return table;
    }

    private static void Add(
        Dictionary<FileKind, FileKindIcon> table,
        FileKind kind,
        string key,
        string hex,
        string body,
        string? shade,
        string? glyph) =>
        table[kind] = new FileKindIcon(key, Color.Parse("#" + hex), body, shade, glyph);
}
