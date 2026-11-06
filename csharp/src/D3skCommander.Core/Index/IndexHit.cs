namespace D3skCommander.Core.Index;

/// <summary>One indexed entry as a query returns it. The path is rebuilt from the parents on demand.</summary>
public sealed record IndexHit(int Id, string Name, string Folder, string Path, bool IsFolder, long Size, DateTimeOffset? Modified);

/// <summary>The best <see cref="Hits"/> of a query and how many entries matched in all.</summary>
public sealed record IndexSearchResult(IReadOnlyList<IndexHit> Hits, int Total)
{
    public static IndexSearchResult None { get; } = new([], 0);
}
