namespace D3skCommander.Core.Index;

/// <summary>When a saved index is old enough to rebuild at start.</summary>
public static class IndexRefresh
{
    /// <summary>
    /// True when an index exists (<paramref name="builtAt"/> known), <paramref name="hours"/> is above 0 and that
    /// many hours have passed. Never for a missing index: the first build is the user's to start.
    /// </summary>
    public static bool IsDue(DateTimeOffset? builtAt, DateTimeOffset now, int hours) =>
        hours > 0 && builtAt is { } built && now - built >= TimeSpan.FromHours(hours);
}
