using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Search;

/// <summary>The "exclude folders" entries of a query: name masks and absolute paths (a path covers what is below it).</summary>
internal sealed class ExcludedFolders
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly Mask? _names;
    private readonly string[] _paths;

    public ExcludedFolders(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var names = new List<string>();
        var paths = new List<string>();
        foreach (var entry in entries.Select(static e => e.Trim()).Where(static e => e.Length > 0))
        {
            if (Path.IsPathRooted(entry))
            {
                paths.Add(Trim(entry));
            }
            else
            {
                names.Add(entry);
            }
        }

        _names = names.Count == 0 ? null : new Mask(string.Join(';', names));
        _paths = [.. paths];
    }

    public bool Contains(string name, string path)
    {
        if (_names is not null && _names.Matches(name))
        {
            return true;
        }

        foreach (var excluded in _paths)
        {
            if (path.Equals(excluded, Comparison)
                || (path.StartsWith(excluded, Comparison) && IsBoundary(excluded, path)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBoundary(string excluded, string path) =>
        excluded[^1] is '/' or '\\' || path[excluded.Length] is '/' or '\\';

    private static string Trim(string path)
    {
        var trimmed = path.TrimEnd('/', Path.DirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}
