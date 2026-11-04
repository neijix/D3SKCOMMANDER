using System.Text;
using System.Text.RegularExpressions;

namespace D3skCommander.Core.Wildcard;

// File-name masks: "*.txt;*.md", "a?.log", "*.rs | test_*" (includes '|' excludes).
// Matching is case-insensitive. A mask with no '*' or '?' matches the whole name.
// An include of "*" or "*.*" matches every name, even one with no dot. The glob
// "*.*" itself still requires a dot, which is what an exclude of "*.*" uses.
public sealed class Mask
{
    // One Unicode scalar. .NET '.' is one UTF-16 code unit, not one Rust char.
    private const string OneScalar = "(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])";

    // The default engine backtracks; Rust's matcher did not.
    private const RegexOptions Options = RegexOptions.NonBacktracking;

    private readonly Glob[] _includes;
    private readonly Glob[] _excludes;
    private readonly bool _matchAll;

    public Mask(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var pipe = pattern.IndexOf('|');
        var includePart = pipe < 0 ? pattern : pattern[..pipe];
        var excludePart = pipe < 0 ? string.Empty : pattern[(pipe + 1)..];
        _includes = SplitGlobs(includePart);
        _excludes = SplitGlobs(excludePart);
        _matchAll = _includes.Length == 0 || _includes.Any(static glob => glob.IsEverything);
    }

    /// <summary>True when every name is accepted: no exclude, and an everything include or none.</summary>
    public bool IsMatchAll => _matchAll && _excludes.Length == 0;

    public bool Matches(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var folded = name.ToLowerInvariant();
        if (_excludes.Any(glob => glob.Matches(folded)))
        {
            return false;
        }

        return _matchAll || _includes.Any(glob => glob.Matches(folded));
    }

    private static Glob[] SplitGlobs(string part)
    {
        var pieces = part.Contains(';') ? part.Split(';') : SplitWhitespace(part);
        return pieces
            .Select(piece => piece.Trim())
            .Where(piece => piece.Length > 0)
            .Select(piece => new Glob(piece))
            .ToArray();
    }

    // Rust splits on Unicode whitespace when the side has no ';'.
    private static string[] SplitWhitespace(string part)
    {
        var pieces = new List<string>();
        var start = 0;
        for (var i = 0; i < part.Length; i++)
        {
            if (!char.IsWhiteSpace(part[i]))
            {
                continue;
            }

            if (i > start)
            {
                pieces.Add(part[start..i]);
            }

            start = i + 1;
        }

        if (start < part.Length)
        {
            pieces.Add(part[start..]);
        }

        return pieces.ToArray();
    }

    private sealed class Glob
    {
        private readonly Regex _regex;

        public Glob(string pattern)
        {
            var folded = pattern.ToLowerInvariant();
            IsEverything = folded is "*" or "*.*";
            _regex = new Regex(ToRegex(folded), Options);
        }

        public bool IsEverything { get; }

        public bool Matches(string foldedName) => _regex.IsMatch(foldedName);

        private static string ToRegex(string folded)
        {
            var pattern = new StringBuilder();
            pattern.Append(@"\A");
            foreach (var rune in folded.EnumerateRunes())
            {
                if (rune.Value == '*')
                {
                    pattern.Append(OneScalar);
                    pattern.Append('*');
                }
                else if (rune.Value == '?')
                {
                    pattern.Append(OneScalar);
                }
                else
                {
                    pattern.Append(Regex.Escape(rune.ToString()));
                }
            }

            pattern.Append(@"\z");
            return pattern.ToString();
        }
    }
}
