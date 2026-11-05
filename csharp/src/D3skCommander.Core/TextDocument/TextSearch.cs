using System.Text;

namespace D3skCommander.Core.TextDocument;

/// <summary>Find and replace over an LF buffer. Ranges are UTF-8 byte offsets, and a search wraps.</summary>
public static class TextSearch
{
    /// <summary>
    /// Byte length of <paramref name="needle"/> at <paramref name="byteOffset"/>,
    /// or null when the text does not start with it there.
    /// <paramref name="matchCase"/> false folds each character's lowercase form.
    /// </summary>
    public static int? MatchLength(string text, int byteOffset, string needle, bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(needle);
        if (needle.Length == 0)
        {
            return null;
        }

        var byteIndex = 0;
        var charIndex = 0;
        while (charIndex < text.Length && byteIndex < byteOffset)
        {
            var skip = Rune.GetRuneAt(text, charIndex);
            byteIndex += skip.Utf8SequenceLength;
            charIndex += skip.Utf16SequenceLength;
        }

        if (byteIndex != byteOffset)
        {
            return null;
        }

        return MatchAt(text, charIndex, needle, matchCase);
    }

    /// <summary>First match at or after <paramref name="from"/>, wrapping to the start. An empty needle matches nothing.</summary>
    public static Range? FindNext(string text, string needle, bool matchCase, int from)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(needle);
        if (needle.Length == 0)
        {
            return null;
        }

        var length = TextIndex.ByteLength(text);
        if (from < 0)
        {
            from = 0;
        }

        if (from > length)
        {
            from = length;
        }

        var start = TextIndex.NextBoundary(text, from);
        var hit = Scan(text, needle, matchCase, start, length);
        if (hit is not null)
        {
            return hit;
        }

        if (start == 0)
        {
            return null;
        }

        return Scan(text, needle, matchCase, 0, start);
    }

    /// <summary>Replaces every non-overlapping match. An empty needle replaces nothing.</summary>
    public static (string Text, int Count) ReplaceAll(string text, string find, string replacement, bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(replacement);
        if (find.Length == 0)
        {
            return (text, 0);
        }

        var builder = new StringBuilder(text.Length);
        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            var matched = MatchAt(text, index, find, matchCase);
            if (matched is int bytes)
            {
                builder.Append(replacement);
                count++;
                index = Skip(text, index, bytes);
            }
            else
            {
                var rune = Rune.GetRuneAt(text, index);
                builder.Append(text.AsSpan(index, rune.Utf16SequenceLength));
                index += rune.Utf16SequenceLength;
            }
        }

        return (builder.ToString(), count);
    }

    private static Range? Scan(string text, string needle, bool matchCase, int fromByte, int untilByte)
    {
        var byteIndex = 0;
        var charIndex = 0;
        while (charIndex < text.Length && byteIndex < fromByte)
        {
            var skip = Rune.GetRuneAt(text, charIndex);
            byteIndex += skip.Utf8SequenceLength;
            charIndex += skip.Utf16SequenceLength;
        }

        while (charIndex < text.Length && byteIndex < untilByte)
        {
            var matched = MatchAt(text, charIndex, needle, matchCase);
            if (matched is int length)
            {
                return new Range(byteIndex, byteIndex + length);
            }

            var rune = Rune.GetRuneAt(text, charIndex);
            byteIndex += rune.Utf8SequenceLength;
            charIndex += rune.Utf16SequenceLength;
        }

        return null;
    }

    private static int? MatchAt(string text, int charIndex, string needle, bool matchCase)
    {
        if (needle.Length == 0)
        {
            return null;
        }

        var index = charIndex;
        var bytes = 0;
        foreach (var expect in needle.EnumerateRunes())
        {
            if (index >= text.Length)
            {
                return null;
            }

            var rune = Rune.GetRuneAt(text, index);
            if (!SameLetter(rune, expect, matchCase))
            {
                return null;
            }

            bytes += rune.Utf8SequenceLength;
            index += rune.Utf16SequenceLength;
        }

        return bytes;
    }

    private static int Skip(string text, int charIndex, int bytes)
    {
        var seen = 0;
        var index = charIndex;
        while (seen < bytes)
        {
            var rune = Rune.GetRuneAt(text, index);
            seen += rune.Utf8SequenceLength;
            index += rune.Utf16SequenceLength;
        }

        return index;
    }

    private static bool SameLetter(Rune left, Rune right, bool matchCase)
    {
        if (left.Value == right.Value)
        {
            return true;
        }

        if (matchCase)
        {
            return false;
        }

        return string.Equals(Lower(left), Lower(right), StringComparison.Ordinal);
    }

    private static string Lower(Rune rune) => rune.ToString().ToLowerInvariant();
}
