using System.Globalization;
using D3skCommander.Core.Splitter;

namespace D3skCommander.Core.Search;

/// <summary>Why a form could not become a query. The window turns each into a localized line.</summary>
public enum SearchFormProblem
{
    None,
    NoFolder,
    NoKind,
    BadSize,
    SizeRange,
    BadDays,
}

public readonly struct SearchQueryResult
{
    private SearchQueryResult(SearchQuery? query, SearchFormProblem problem, string detail)
    {
        Query = query;
        Problem = problem;
        Detail = detail;
    }

    public SearchQuery? Query { get; }

    public SearchFormProblem Problem { get; }

    /// <summary>The text that was refused (a size or a number of days), or empty.</summary>
    public string Detail { get; }

    public bool IsSuccess => Query is not null;

    public static SearchQueryResult Ok(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new SearchQueryResult(query, SearchFormProblem.None, "");
    }

    public static SearchQueryResult Fail(SearchFormProblem problem, string detail = "") =>
        new(null, problem, detail);
}

/// <summary>Turns the form's fields into a query. <c>now</c> is a parameter so the day bounds are testable.</summary>
public static class SearchQueryBuilder
{
    public static SearchQueryResult Build(SearchForm form, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(form);
        var roots = SplitRoots(form.Roots);
        if (roots.Count == 0)
        {
            return SearchQueryResult.Fail(SearchFormProblem.NoFolder);
        }

        if (!form.Files && !form.Dirs)
        {
            return SearchQueryResult.Fail(SearchFormProblem.NoKind);
        }

        if (!TrySize(form.MinSize, out var min, out var badMin))
        {
            return SearchQueryResult.Fail(SearchFormProblem.BadSize, badMin);
        }

        if (!TrySize(form.MaxSize, out var max, out var badMax))
        {
            return SearchQueryResult.Fail(SearchFormProblem.BadSize, badMax);
        }

        if (min is ulong low && max is ulong high && low > high)
        {
            return SearchQueryResult.Fail(SearchFormProblem.SizeRange);
        }

        if (!TryDays(form.NewerDays, now, out var newer, out var badNewer))
        {
            return SearchQueryResult.Fail(SearchFormProblem.BadDays, badNewer);
        }

        if (!TryDays(form.OlderDays, now, out var older, out var badOlder))
        {
            return SearchQueryResult.Fail(SearchFormProblem.BadDays, badOlder);
        }

        var name = form.Name.Trim();
        return SearchQueryResult.Ok(new SearchQuery
        {
            Roots = roots,
            Name = name.Length == 0 ? "*" : name,
            NameIsRegex = form.NameIsRegex,
            Content = form.Text.Length == 0 ? null : form.Text,
            ContentIsRegex = form.TextIsRegex,
            CaseSensitive = form.CaseSensitive,
            WholeWords = form.WholeWords,
            NotContaining = form.NotContaining,
            ExcludeFiles = form.ExcludeFiles.Trim(),
            ExcludeFolders = SplitRoots(form.ExcludeFolders),
            FollowLinks = form.FollowLinks,
            MaxDepth = form.DepthLimited ? Math.Max(form.Depth, 1) : null,
            IncludeHidden = form.IncludeHidden,
            MinSize = min,
            MaxSize = max,
            NewerThan = newer,
            OlderThan = older,
            Files = form.Files,
            Dirs = form.Dirs,
            SearchArchives = form.InArchives,
        });
    }

    public static IReadOnlyList<string> SplitRoots(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TrySize(string text, out ulong? bytes, out string bad)
    {
        bytes = null;
        bad = "";
        if (text.Trim().Length == 0)
        {
            return true;
        }

        var parsed = SplitSize.Parse(text);
        if (!parsed.IsSuccess)
        {
            bad = text.Trim();
            return false;
        }

        bytes = parsed.Bytes;
        return true;
    }

    private static bool TryDays(string text, DateTimeOffset now, out DateTimeOffset? limit, out string bad)
    {
        limit = null;
        bad = "";
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            || days > (uint)(now - DateTimeOffset.MinValue).TotalDays)
        {
            bad = trimmed;
            return false;
        }

        limit = now.AddDays(-days);
        return true;
    }
}
