using D3skCommander.Core.Common;

namespace D3skCommander.Core.Search;

public enum SearchEventKind
{
    Hit,
    Scanned,
    Error,
    Done,
}

/// <summary>One matching entry. <see cref="Line"/> is the first matching line (1-based) of a content search.</summary>
public sealed record SearchHit(
    string Path,
    string Name,
    bool IsDirectory,
    ulong Size,
    DateTimeOffset? Modified,
    int? Line,
    string? LineText);

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

    public static SearchEvent ForDone(long hits, bool cancelled) =>
        new(SearchEventKind.Done) { Hits = hits, Cancelled = cancelled };
}
