using System.Text;

namespace D3skCommander.Core.TextDocument;

/// <summary>UTF-8 byte offsets into a .NET string. v0.1 indexes the editor buffer that way.</summary>
public static class TextIndex
{
    public static int ByteLength(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Encoding.UTF8.GetByteCount(text);
    }

    /// <summary>Byte offset of the rune at <paramref name="charIndex"/>, or the byte length when past the end.</summary>
    public static int CharToByte(string text, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (charIndex <= 0)
        {
            return 0;
        }

        var seen = 0;
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count == charIndex)
            {
                return seen;
            }

            seen += rune.Utf8SequenceLength;
            count++;
        }

        return seen;
    }

    /// <summary>How many runes are in the prefix of <paramref name="byteIndex"/> bytes.</summary>
    public static int ByteToChar(string text, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(text);
        var limit = byteIndex;
        var length = ByteLength(text);
        if (limit > length)
        {
            limit = length;
        }

        if (limit < 0)
        {
            limit = 0;
        }

        var count = 0;
        var seen = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (seen >= limit)
            {
                break;
            }

            var next = seen + rune.Utf8SequenceLength;
            if (next > limit)
            {
                break;
            }

            seen = next;
            count++;
        }

        return count;
    }

    /// <summary>The next rune boundary at or after <paramref name="byteIndex"/>.</summary>
    public static int NextBoundary(string text, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (byteIndex <= 0)
        {
            return 0;
        }

        var seen = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (seen >= byteIndex)
            {
                return seen;
            }

            seen += rune.Utf8SequenceLength;
        }

        return seen;
    }

    /// <summary>Replaces the UTF-8 byte range. Both ends must be rune boundaries.</summary>
    public static string Replace(string text, int startByte, int endByte, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(replacement);
        if (startByte < 0 || endByte < startByte)
        {
            throw new ArgumentOutOfRangeException(nameof(startByte));
        }

        var byteIndex = 0;
        var charIndex = 0;
        var startChar = -1;
        while (charIndex <= text.Length)
        {
            if (byteIndex == startByte && startChar < 0)
            {
                startChar = charIndex;
            }

            if (byteIndex == endByte && startChar >= 0)
            {
                var removed = text.Remove(startChar, charIndex - startChar);
                return removed.Insert(startChar, replacement);
            }

            if (charIndex >= text.Length)
            {
                break;
            }

            var rune = Rune.GetRuneAt(text, charIndex);
            byteIndex += rune.Utf8SequenceLength;
            charIndex += rune.Utf16SequenceLength;
        }

        throw new ArgumentOutOfRangeException(nameof(endByte), endByte, "The range is not on character boundaries.");
    }
}
