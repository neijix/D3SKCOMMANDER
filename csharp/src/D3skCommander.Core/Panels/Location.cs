using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Panels;

/// <summary>
/// The folder a tab is showing. A path inside an archive file is that archive.
/// <see cref="Provider"/>, when set, is a virtual folder (search results or a branch view)
/// and wins over the tab's usual provider.
/// </summary>
public sealed class Location : IEquatable<Location>
{
    public Location(string path, IFileSystemProvider? provider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
        Provider = provider;
    }

    public string Path { get; }

    /// <summary>The provider that lists this folder, when it is not the tab's usual one.</summary>
    public IFileSystemProvider? Provider { get; }

    public bool Equals(Location? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!string.Equals(Path, other.Path, StringComparison.Ordinal))
        {
            return false;
        }

        return ReferenceEquals(Provider, other.Provider);
    }

    public override bool Equals(object? obj) => Equals(obj as Location);

    public override int GetHashCode()
    {
        var hash = Path.GetHashCode(StringComparison.Ordinal);
        if (Provider is null)
        {
            return hash;
        }

        return HashCode.Combine(hash, Provider);
    }

    public override string ToString() => Path;

    /// <summary>The folder above this one, or null at a root.</summary>
    public Location? Parent()
    {
        var parent = ParentPath(Path);
        if (string.IsNullOrEmpty(parent))
        {
            return null;
        }

        return new Location(parent);
    }

    // Same trimming as the local listing's ".." row, so going up hits that folder.
    private static string? ParentPath(string path)
    {
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(path);
        if (trimmed.Length == 0)
        {
            return null;
        }

        return System.IO.Path.GetDirectoryName(trimmed);
    }
}
