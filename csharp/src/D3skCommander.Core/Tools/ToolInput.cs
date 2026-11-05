using System.Globalization;

namespace D3skCommander.Core.Tools;

/// <summary>Shared input for the time tools: local dates, and folders expanded to the files inside them.</summary>
public static class ToolInput
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];

    /// <summary><c>YYYY-MM-DD</c>, <c>YYYY-MM-DD HH:MM</c> or <c>YYYY-MM-DD HH:MM:SS</c>, in local time.</summary>
    public static DateParseResult ParseDate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (!DateTime.TryParseExact(
                trimmed,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var naive))
        {
            return DateParseResult.Fail($"Invalid date: {trimmed} (YYYY-MM-DD HH:MM:SS)");
        }

        var unspecified = DateTime.SpecifyKind(naive, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.Local;
        if (zone.IsInvalidTime(unspecified))
        {
            return DateParseResult.Fail($"Invalid date: {trimmed}");
        }

        var offset = zone.IsAmbiguousTime(unspecified)
            ? EarliestOffset(zone, unspecified)
            : zone.GetUtcOffset(unspecified);
        return DateParseResult.Ok(new DateTimeOffset(unspecified, offset));
    }

    /// <summary>The paths with each folder replaced by every file inside it. A symlink is not followed.</summary>
    public static List<string> ExpandFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var files = new List<string>();
        foreach (var path in paths)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (IsDirectory(path))
            {
                Collect(path, files);
            }
            else if (IsFile(path))
            {
                files.Add(path);
            }
        }

        return files;
    }

    // chrono's earliest() keeps the first instant when a local time happens twice.
    private static TimeSpan EarliestOffset(TimeZoneInfo zone, DateTime unspecified)
    {
        var offsets = zone.GetAmbiguousTimeOffsets(unspecified);
        var chosen = offsets[0];
        var chosenUtc = unspecified - chosen;
        for (var i = 1; i < offsets.Length; i++)
        {
            var utc = unspecified - offsets[i];
            if (utc < chosenUtc)
            {
                chosen = offsets[i];
                chosenUtc = utc;
            }
        }

        return chosen;
    }

    private static void Collect(string directory, List<string> files)
    {
        List<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directory, "*", Listing()).ToList();
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return;
        }

        foreach (var child in children)
        {
            // walkdir's default does not follow symlinks, and a symlink is not a file.
            if (IsLink(child))
            {
                continue;
            }

            if (IsDirectory(child))
            {
                Collect(child, files);
                continue;
            }

            if (IsFile(child))
            {
                files.Add(child);
            }
        }
    }

    private static EnumerationOptions Listing() => new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private static bool IsDirectory(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool IsFile(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
