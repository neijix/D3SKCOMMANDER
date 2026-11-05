using System.Text.RegularExpressions;
using D3skCommander.Core.Common;
using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Search;

/// <summary>The name test and the content test of one query, compiled once. Regexes never backtrack.</summary>
internal sealed class SearchMatchers
{
    private const RegexOptions Base = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

    private readonly Mask? _mask;
    private readonly Regex? _nameRegex;

    private SearchMatchers(Mask? mask, Regex? nameRegex, Regex? content, bool contentIsPlain)
    {
        _mask = mask;
        _nameRegex = nameRegex;
        Content = content;
        ContentIsPlain = contentIsPlain;
    }

    public Regex? Content { get; }

    /// <summary>Plain text is searched even in binary files; a regex skips them.</summary>
    public bool ContentIsPlain { get; }

    public static (SearchMatchers? Matchers, OperationError? Error) Build(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Mask? mask = null;
        Regex? nameRegex = null;
        if (query.NameIsRegex)
        {
            var built = Compile(query.Name, Options(query));
            if (built.Regex is null)
            {
                return (null, OperationError.InvalidData("invalid name regex: " + built.Problem));
            }

            nameRegex = built.Regex;
        }
        else
        {
            mask = new Mask(query.Name);
        }

        Regex? content = null;
        var plain = !query.ContentIsRegex;
        if (!string.IsNullOrEmpty(query.Content))
        {
            var pattern = query.ContentIsRegex ? query.Content : Regex.Escape(query.Content);
            if (query.WholeWords)
            {
                pattern = @"\b(?:" + pattern + @")\b";
            }

            var built = Compile(pattern, Options(query));
            if (built.Regex is null)
            {
                return (null, OperationError.InvalidData("invalid content pattern: " + built.Problem));
            }

            content = built.Regex;
        }

        return (new SearchMatchers(mask, nameRegex, content, plain), null);
    }

    public bool NameMatches(string name)
    {
        if (_nameRegex is not null)
        {
            return _nameRegex.IsMatch(name);
        }

        return _mask is null || _mask.Matches(name);
    }

    private static RegexOptions Options(SearchQuery query) =>
        query.CaseSensitive ? Base : Base | RegexOptions.IgnoreCase;

    private static (Regex? Regex, string Problem) Compile(string pattern, RegexOptions options)
    {
        try
        {
            return (new Regex(pattern, options), "");
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return (null, ex.Message);
        }
    }
}
