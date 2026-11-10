namespace D3skCommander.Core.FileOps;

/// <summary>
/// Whether a file the app holds open (a video playing in the preview) is one of the paths an operation is about
/// to delete, or lies inside one of its folders, so the app lets go of it first.
/// </summary>
public static class HeldFiles
{
    /// <summary>True when <paramref name="held"/> is one of <paramref name="paths"/> or under one of them.</summary>
    public static bool Covers(IEnumerable<string> paths, string? held)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrEmpty(held))
        {
            return false;
        }

        var file = FileOpNames.Full(held);
        foreach (var path in paths)
        {
            if (path.Length > 0 && FileOpNames.IsInside(FileOpNames.Full(path), file))
            {
                return true;
            }
        }

        return false;
    }
}
