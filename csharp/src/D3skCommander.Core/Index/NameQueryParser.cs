using System.Globalization;
using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Index;

/// <summary>
/// Reads the quick-find box. Words are separated by spaces ("double quotes" keep a space in a word) and ANDed:
/// a plain word is a case-insensitive substring of the name; a word with '*' or '?' is a wildcard on the name;
/// <c>ext:pdf;docx</c>, <c>size:&gt;100M</c> / <c>size:&lt;1K</c> / <c>size:10M..1G</c>, <c>folder:text</c> (a substring of the
/// parent path) and <c>type:file|folder</c> filter. A filter that cannot be read is ignored, so a half-typed
/// <c>size:&gt;</c> does not blank the list.
/// </summary>
public static class NameQueryParser
{
    private const long Kilo = 1024;

    public static NameQuery Parse(string? text)
    {
        var terms = new List<NameTerm>();
        var extensions = new List<string>();
        var folders = new List<string>();
        var ignored = new List<string>();
        SizeRange? size = null;
        var kind = NameKind.Any;
        foreach (var word in Words(text ?? ""))
        {
            var colon = word.IndexOf(':', StringComparison.Ordinal);
            var key = colon > 0 ? word[..colon].ToLowerInvariant() : "";
            var value = colon > 0 ? word[(colon + 1)..] : "";
            switch (key)
            {
                case "ext":
                    AddExtensions(value, extensions);
                    break;
                case "size":
                    size = ReadSize(value, size, ignored, word);
                    break;
                case "folder":
                    AddIf(value, folders);
                    break;
                case "type":
                    kind = ReadKind(value, kind, ignored, word);
                    break;
                default:
                    terms.Add(Term(word));
                    break;
            }
        }

        return new NameQuery(terms, extensions, size, folders, kind, ignored);
    }

    /// <summary>A byte count such as "100M", "1.5G", "512", "4KB"; K, M, G, T are powers of 1024.</summary>
    public static bool TryParseBytes(string text, out long bytes)
    {
        bytes = 0;
        var trimmed = text.Trim();
        var digits = 0;
        while (digits < trimmed.Length && (char.IsAsciiDigit(trimmed[digits]) || trimmed[digits] == '.'))
        {
            digits++;
        }

        if (digits == 0 || !double.TryParse(trimmed[..digits], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var unit = trimmed[digits..].Trim().ToLowerInvariant();
        if (unit.EndsWith("ib", StringComparison.Ordinal))
        {
            unit = unit[..^2];
        }
        else if (unit.EndsWith('b'))
        {
            unit = unit[..^1];
        }

        var factor = unit switch
        {
            "" => 1d,
            "k" => Kilo,
            "m" => Kilo * Kilo,
            "g" => Kilo * Kilo * Kilo,
            "t" => Kilo * Kilo * Kilo * Kilo,
            _ => -1d,
        };
        if (factor < 0 || number * factor >= long.MaxValue)
        {
            return false;
        }

        bytes = (long)(number * factor);
        return true;
    }

    private static NameTerm Term(string word) =>
        word.AsSpan().IndexOfAny('*', '?') >= 0 ? new NameTerm(word, new Mask(word)) : new NameTerm(word, null);

    private static void AddIf(string value, List<string> list)
    {
        if (value.Length > 0)
        {
            list.Add(value);
        }
    }

    private static void AddExtensions(string value, List<string> extensions)
    {
        foreach (var part in value.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var extension = part.TrimStart('*', '.').ToLowerInvariant();
            if (extension.Length > 0)
            {
                extensions.Add(extension);
            }
        }
    }

    private static NameKind ReadKind(string value, NameKind current, List<string> ignored, string word)
    {
        switch (value.ToLowerInvariant())
        {
            case "file" or "files":
                return NameKind.File;
            case "folder" or "folders" or "dir" or "dirs":
                return NameKind.Folder;
            case "":
                return current;
            default:
                ignored.Add(word);
                return current;
        }
    }

    private static SizeRange? ReadSize(string value, SizeRange? current, List<string> ignored, string word)
    {
        if (value.Length == 0)
        {
            return current;
        }

        if (!TryParseRange(value, out var range))
        {
            ignored.Add(word);
            return current;
        }

        return current is { } before ? before.Intersect(range) : range;
    }

    private static bool TryParseRange(string value, out SizeRange range)
    {
        range = SizeRange.Any;
        var dots = value.IndexOf("..", StringComparison.Ordinal);
        if (dots >= 0)
        {
            return TryBound(value[..dots], 0, out var low)
                && TryBound(value[(dots + 2)..], long.MaxValue, out var high)
                && Set(low, high, out range);
        }

        if (value.StartsWith(">=", StringComparison.Ordinal))
        {
            return TryParseBytes(value[2..], out var atLeast) && Set(atLeast, long.MaxValue, out range);
        }

        if (value.StartsWith("<=", StringComparison.Ordinal))
        {
            return TryParseBytes(value[2..], out var atMost) && Set(0, atMost, out range);
        }

        if (value.StartsWith('>'))
        {
            return TryParseBytes(value[1..], out var over) && over < long.MaxValue && Set(over + 1, long.MaxValue, out range);
        }

        if (value.StartsWith('<'))
        {
            return TryParseBytes(value[1..], out var under) && under > 0 && Set(0, under - 1, out range);
        }

        return TryParseBytes(value, out var exact) && Set(exact, exact, out range);
    }

    private static bool TryBound(string text, long empty, out long bytes)
    {
        if (text.Trim().Length == 0)
        {
            bytes = empty;
            return true;
        }

        return TryParseBytes(text, out bytes);
    }

    private static bool Set(long min, long max, out SizeRange range)
    {
        range = new SizeRange(min, max);
        return min <= max;
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                Flush(words, current);
            }
            else
            {
                current.Append(c);
            }
        }

        Flush(words, current);
        return words;
    }

    private static void Flush(List<string> words, System.Text.StringBuilder current)
    {
        if (current.Length > 0)
        {
            words.Add(current.ToString());
            current.Clear();
        }
    }
}
