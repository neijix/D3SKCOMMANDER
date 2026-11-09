namespace D3skCommander.Core.Settings;

/// <summary>What a favorite (a hotlist item) leads to once its path is probed.</summary>
public enum HotlistTargetKind
{
    /// <summary>The path is a folder: open it.</summary>
    Folder,

    /// <summary>The path is a file: open its folder with the cursor on it.</summary>
    File,

    /// <summary>Neither a folder nor a file exists at the path.</summary>
    Missing,
}

/// <summary>
/// The resolution of a favorite: where to go (<see cref="Folder"/>), which entry to put the cursor on
/// (<see cref="CursorName"/>, files only), or that it is gone (<see cref="Path"/> carries the path).
/// </summary>
public sealed record HotlistTarget(HotlistTargetKind Kind, string Folder, string? CursorName, string Path)
{
    /// <summary>
    /// Resolves <paramref name="item"/> with the existence probes passed in (folder first, so a folder wins a tie).
    /// The JSON has no kind field: file or folder is probed when the list is shown and when an item is picked.
    /// </summary>
    public static HotlistTarget Resolve(HotlistItem item, Func<string, bool> folderExists, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(folderExists);
        ArgumentNullException.ThrowIfNull(fileExists);
        var path = item.Path;
        if (folderExists(path))
        {
            return new HotlistTarget(HotlistTargetKind.Folder, path, null, path);
        }

        if (fileExists(path))
        {
            var folder = System.IO.Path.GetDirectoryName(path) ?? "";
            return new HotlistTarget(HotlistTargetKind.File, folder, System.IO.Path.GetFileName(path), path);
        }

        return new HotlistTarget(HotlistTargetKind.Missing, "", null, path);
    }
}
