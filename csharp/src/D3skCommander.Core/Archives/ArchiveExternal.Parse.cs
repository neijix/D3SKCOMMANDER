using System.Globalization;

namespace D3skCommander.Core.Archives;

public static partial class ArchiveExternal
{
    /// <summary>Parses <c>7z l -slt</c>: a header, a <c>----------</c> line, then one block per member.</summary>
    public static IReadOnlyList<ArchiveEntry> ParseSlt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string Marker = "\n----------";
        var marker = normalized.IndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return [];
        }

        var body = normalized[(marker + Marker.Length)..];
        var entries = new List<ArchiveEntry>();
        foreach (var block in body.Split("\n\n"))
        {
            var entry = ReadBlock(block);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Parses <c>unrar lb</c> bare names. A name that is another's parent is a folder.</summary>
    public static IReadOnlyList<ArchiveEntry> ParseLb(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var names = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var name = ArchivePaths.Normalize(line.TrimEnd('\r'));
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        var entries = new List<ArchiveEntry>(names.Count);
        foreach (var name in names)
        {
            entries.Add(new ArchiveEntry(name, 0, null, IsParent(names, name), null));
        }

        return entries;
    }

    private static ArchiveEntry? ReadBlock(string block)
    {
        var fields = new SltFields();
        foreach (var line in block.Split('\n'))
        {
            var split = line.IndexOf(" = ", StringComparison.Ordinal);
            if (split < 0)
            {
                continue;
            }

            fields.Apply(line[..split].Trim(), line[(split + 3)..].TrimEnd('\r'));
        }

        if (string.IsNullOrEmpty(fields.Path))
        {
            return null;
        }

        return new ArchiveEntry(fields.Path, fields.Size, fields.Packed, fields.IsDir, fields.Modified);
    }

    private static bool IsParent(List<string> names, string name)
    {
        var prefix = name + "/";
        foreach (var other in names)
        {
            if (other.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private struct SltFields
    {
        public string? Path;
        public long Size;
        public long? Packed;
        public bool IsDir;
        public DateTimeOffset? Modified;

        public void Apply(string key, string value)
        {
            if (key == "Path")
            {
                Path = ArchivePaths.Normalize(value);
            }
            else if (key == "Size")
            {
                Size = ParseSize(value);
            }
            else if (key == "Packed Size")
            {
                Packed = ParsePacked(value);
            }
            else if (key == "Modified")
            {
                Modified = ParseTime(value);
            }
            else if (key == "Folder")
            {
                IsDir = IsDir || value == "+";
            }
            else if (key == "Attributes")
            {
                IsDir = IsDir || (value.Length > 0 && value[0] == 'D');
            }
        }
    }

    private static long ParseSize(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size >= 0)
        {
            return size;
        }

        return 0;
    }

    private static long? ParsePacked(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size >= 0)
        {
            return size;
        }

        return null;
    }

    private static DateTimeOffset? ParseTime(string value)
    {
        if (value.Length < 19)
        {
            return null;
        }

        if (!DateTime.TryParseExact(
            value[..19],
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var naive))
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(naive, DateTimeKind.Local));
    }
}
