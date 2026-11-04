namespace D3skCommander.Core.Panels;

/// <summary>
/// The folder a tab is showing. Archive and search-result locations are a later row;
/// this one does not browse archives.
/// </summary>
public sealed class Location : IEquatable<Location>
{
    public Location(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    public string Path { get; }

    public bool Equals(Location? other) =>
        other is not null && string.Equals(Path, other.Path, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Location);

    public override int GetHashCode() => Path.GetHashCode(StringComparison.Ordinal);

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
