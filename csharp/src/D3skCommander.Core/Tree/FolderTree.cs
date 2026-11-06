using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Tree;

/// <summary>The path rules of the folder tree panel: which root holds a folder, the chain down to it, the children of a listing.</summary>
public static class FolderTree
{
    /// <summary>The longest root that is <paramref name="path"/> or a parent of it; null when none is. Handles '/' and '\' paths.</summary>
    public static string? RootFor(string path, IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(roots);
        string? best = null;
        var bestLength = -1;
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Holds(root, path))
            {
                continue;
            }

            var length = Trimmed(root).Length;
            if (length > bestLength)
            {
                best = root;
                bestLength = length;
            }
        }

        return best;
    }

    /// <summary>
    /// The folders from <paramref name="root"/> down to <paramref name="path"/>, each as a full path, the root first (as given)
    /// and the path last. Empty when <paramref name="path"/> is not under <paramref name="root"/>.
    /// </summary>
    public static IReadOnlyList<string> ChainFrom(string root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        if (root.Length == 0 || !Holds(root, path))
        {
            return [];
        }

        var chain = new List<string> { root };
        var separator = SeparatorOf(root, path);
        var current = Trimmed(root);
        var rest = Trimmed(path)[current.Length..];
        foreach (var segment in rest.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            current = current + separator + segment;
            chain.Add(current);
        }

        return chain;
    }

    /// <summary>Whether two paths name the same folder: trailing separators are ignored, and the case too for a Windows-style path.</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(Trimmed(a), Trimmed(b), IsWindowsStyle(a) || IsWindowsStyle(b) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The folders of <paramref name="listing"/> (links to folders included), sorted by name without regard to case.</summary>
    public static IReadOnlyList<FolderChild> ChildFolders(IEnumerable<Entry> listing, bool showHidden)
    {
        ArgumentNullException.ThrowIfNull(listing);
        var folders = new List<FolderChild>();
        foreach (var entry in listing)
        {
            if (entry.IsDirectory && !entry.IsParent && (showHidden || !entry.Hidden))
            {
                folders.Add(new FolderChild(entry.Name, entry.Path));
            }
        }

        folders.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Name, b.Name);
        });
        return folders;
    }

    private static bool Holds(string root, string path)
    {
        var head = Trimmed(root);
        var full = Trimmed(path);
        var comparison = IsWindowsStyle(root) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(head, comparison))
        {
            return false;
        }

        return full.Length == head.Length || full[head.Length] is '/' or '\\';
    }

    // "C:\" and "/" both trim to a prefix a separator follows: "C:" and "".
    private static string Trimmed(string path) => path.TrimEnd('/', '\\');

    private static bool IsWindowsStyle(string path) =>
        (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0])) || path.StartsWith(@"\\", StringComparison.Ordinal);

    private static char SeparatorOf(string root, string path)
    {
        var text = root.Contains('\\') ? root : path;
        return text.Contains('\\') && !text.Contains('/') ? '\\' : text.Contains('/') ? '/' : Path.DirectorySeparatorChar;
    }
}
