using System.Globalization;
using D3skCommander.Core.Localization;

namespace D3skCommander.Core.FileSystem;

/// <summary>The lines of the Properties dialog, one <c>Label: value</c> per fact.</summary>
public static class EntryPropertiesText
{
    /// <summary>One entry. <paramref name="inside"/> is the measured tree of a folder, null for anything else.</summary>
    public static IReadOnlyList<string> ForEntry(ILocalizer text, EntryProperties props, SelectionSize? inside)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(props);
        var lines = new List<string>
        {
            Line(text, "props.name", props.Name),
            Line(text, "props.path", props.FullPath),
            Line(text, "props.type", text.Get(TypeKey(props.Kind))),
        };
        if (props.LinkTarget is not null)
        {
            lines.Add(Line(text, "props.link", props.LinkTarget));
        }

        if (props.Kind == EntryKind.Folder && inside is not null)
        {
            lines.Add(Line(text, "props.size", Bytes(text, inside.Value.Bytes)));
            lines.Add(Line(text, "props.contents", Contents(text, inside.Value)));
        }
        else if (props.Kind != EntryKind.Folder)
        {
            lines.Add(Line(text, "props.size", Bytes(text, props.Size)));
        }

        lines.Add(Line(text, "props.created", LocalFileSystem.FormatTime(props.Created)));
        lines.Add(Line(text, "props.modified", LocalFileSystem.FormatTime(props.Modified)));
        lines.Add(Line(text, "props.accessed", LocalFileSystem.FormatTime(props.Accessed)));
        if (props.Attributes.Length > 0)
        {
            var key = OperatingSystem.IsWindows() ? "props.attributes" : "props.permissions";
            lines.Add(Line(text, key, props.Attributes));
        }

        if (props.Owner is not null)
        {
            lines.Add(Line(text, "props.owner", props.Owner));
        }

        return lines;
    }

    /// <summary>Several entries: how many, where, and the total of their trees.</summary>
    public static IReadOnlyList<string> ForSelection(ILocalizer text, int count, string directory, SelectionSize total)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(directory);
        return
        [
            Line(text, "props.selected", count.ToString(CultureInfo.InvariantCulture)),
            Line(text, "props.path", directory),
            Line(text, "props.size", Bytes(text, total.Bytes)),
            Line(text, "props.contents", Contents(text, total)),
        ];
    }

    private static string Line(ILocalizer text, string labelKey, string value) => text.Get(labelKey) + ": " + value;

    private static string TypeKey(EntryKind kind)
    {
        if (kind == EntryKind.Folder)
        {
            return "props.type_folder";
        }

        return kind == EntryKind.Link ? "props.type_link" : "props.type_file";
    }

    private static string Bytes(ILocalizer text, ulong bytes) =>
        LocalFileSystem.FormatSize(bytes) + " (" + LocalFileSystem.FormatGrouped(bytes) + " " + text.Get("props.bytes") + ")";

    private static string Contents(ILocalizer text, SelectionSize size) =>
        text.Get("props.contents_value")
            .Replace("{0}", LocalFileSystem.FormatGrouped(size.Files), StringComparison.Ordinal)
            .Replace("{1}", LocalFileSystem.FormatGrouped(size.Folders), StringComparison.Ordinal);
}
