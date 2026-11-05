namespace D3skCommander.Core.Ftp;

/// <summary>Remote path rules: joining, sorting and the names that may be used locally.</summary>
public static class FtpPaths
{
    /// <summary><paramref name="name"/> below the remote folder <paramref name="baseFolder"/> (<c>/</c> and relative bases both work).</summary>
    public static string Join(string baseFolder, string name)
    {
        ArgumentNullException.ThrowIfNull(baseFolder);
        ArgumentNullException.ThrowIfNull(name);
        if (baseFolder.Length == 0)
        {
            return name;
        }

        return baseFolder.EndsWith('/') ? baseFolder + name : baseFolder + "/" + name;
    }

    /// <summary>The folder part of a remote path: <c>/a/b</c> gives <c>/a</c>, <c>/a</c> gives <c>/</c>, <c>a</c> gives empty.</summary>
    public static string Parent(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
        var slash = trimmed.LastIndexOf('/');
        if (slash < 0)
        {
            return "";
        }

        return slash == 0 ? "/" : trimmed[..slash];
    }

    /// <summary>The last segment of a remote path. <c>/</c> gives <c>/</c>.</summary>
    public static string Name(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 || trimmed.Length == 1 ? trimmed : trimmed[(slash + 1)..];
    }

    /// <summary>Folders first, then names case-insensitively, then ordinally.</summary>
    public static void Sort(List<RemoteEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        entries.Sort(Compare);
    }

    /// <summary>A server-supplied name is used locally only when it cannot leave the target folder.</summary>
    public static bool IsSafeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || name == "." || name == "..")
        {
            return false;
        }

        if (name.AsSpan().IndexOfAny('/', '\\', '\0') >= 0)
        {
            return false;
        }

        return !(OperatingSystem.IsWindows() && name.Contains(':'));
    }

    private static int Compare(RemoteEntry a, RemoteEntry b)
    {
        if (a.IsDirectory != b.IsDirectory)
        {
            return a.IsDirectory ? -1 : 1;
        }

        var folded = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        return folded != 0 ? folded : string.CompareOrdinal(a.Name, b.Name);
    }
}
