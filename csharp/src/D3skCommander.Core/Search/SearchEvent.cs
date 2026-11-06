using D3skCommander.Core.Common;

namespace D3skCommander.Core.Search;

public enum SearchEventKind
{
    Hit,
    Scanned,
    Error,
    Done,
    Hashing,
}

/// <summary>One matching entry. <see cref="Group"/> is the 1-based duplicate group, 0 outside a duplicates search. <see cref="Line"/> is the first matching line (1-based) of a content search.</summary>
public sealed record SearchHit(
    string Path,
    string Name,
    bool IsDirectory,
    ulong Size,
    DateTimeOffset? Modified,
    int? Line,
    string? LineText,
    int Group = 0);

/// <summary>One step of a running search. A search always ends with <see cref="SearchEventKind.Done"/>.</summary>
public sealed class SearchEvent
{
    private SearchEvent(SearchEventKind kind)
    {
        Kind = kind;
    }

    public SearchEventKind Kind { get; }

    public SearchHit? Hit { get; private init; }

    /// <summary>Entries looked at so far (<see cref="SearchEventKind.Scanned"/>).</summary>
    public long Count { get; private init; }

    public string? Current { get; private init; }

    public OperationError? Error { get; private init; }

    /// <summary>Hits found (<see cref="SearchEventKind.Done"/>).</summary>
    public long Hits { get; private init; }

    public bool Cancelled { get; private init; }

    /// <summary>Files that could not be read while looking for duplicates (<see cref="SearchEventKind.Done"/>).</summary>
    public int Skipped { get; private init; }

    /// <summary>Files hashed so far (<see cref="SearchEventKind.Hashing"/>); <see cref="Count"/> holds the total.</summary>
    public long Hashed { get; private init; }

    public static SearchEvent ForHit(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new SearchEvent(SearchEventKind.Hit) { Hit = hit };
    }

    public static SearchEvent ForScanned(long count, string current) =>
        new(SearchEventKind.Scanned) { Count = count, Current = current };

    public static SearchEvent ForError(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new SearchEvent(SearchEventKind.Error) { Error = error };
    }

    public static SearchEvent ForDone(long hits, bool cancelled, int skipped = 0) =>
        new(SearchEventKind.Done) { Hits = hits, Cancelled = cancelled, Skipped = skipped };

    public static SearchEvent ForHashing(long hashed, long total) =>
        new(SearchEventKind.Hashing) { Hashed = hashed, Count = total };
}
