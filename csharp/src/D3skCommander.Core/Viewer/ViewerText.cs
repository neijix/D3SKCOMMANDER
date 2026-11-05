using System.Text;

namespace D3skCommander.Core.Viewer;

/// <summary>
/// Lister text rules from v0.1 <c>src/ui/lister/text.rs</c>: BOM detection, decoding, line
/// splitting, wrapping, clipping, and search. This is not the editor buffer.
/// </summary>
public static class ViewerText
{
    /// <summary>The encoding a BOM announces, otherwise UTF-8, and the BOM's length.</summary>
    public static ViewerEncodingMark DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            return new ViewerEncodingMark(ViewerEncoding.Utf8Bom, 3);
        }

        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            return new ViewerEncodingMark(ViewerEncoding.Utf16Le, 2);
        }

        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            return new ViewerEncodingMark(ViewerEncoding.Utf16Be, 2);
        }

        return new ViewerEncodingMark(ViewerEncoding.Utf8, 0);
    }

    /// <summary>
    /// True when a NUL sits in the first <see cref="ViewerLimits.Sniff"/> bytes, unless a UTF-16
    /// BOM explains the NULs.
    /// </summary>
    public static bool IsBinary(ReadOnlySpan<byte> bytes)
    {
        var encoding = DetectEncoding(bytes).Encoding;
        if (encoding != ViewerEncoding.Utf8 && encoding != ViewerEncoding.Utf8Bom)
        {
            return false;
        }

        var sniff = Math.Min(bytes.Length, ViewerLimits.Sniff);
        for (var i = 0; i < sniff; i++)
        {
            if (bytes[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> (BOM included). Invalid sequences become U+FFFD and each
    /// tab becomes four spaces, so a monospace column count equals a character count.
    /// </summary>
    public static ViewerDecoded Decode(ReadOnlySpan<byte> bytes)
    {
        var mark = DetectEncoding(bytes);
        var body = mark.BomLength >= bytes.Length ? ReadOnlySpan<byte>.Empty : bytes[mark.BomLength..];
        var text = DecodeBody(body, mark.Encoding);
        if (text.Contains('\t'))
        {
            text = text.Replace("\t", "    ", StringComparison.Ordinal);
        }

        return new ViewerDecoded(text, mark.Encoding);
    }

    /// <summary>Line spans without the terminator (<c>\n</c> or <c>\r\n</c>). A trailing newline adds no empty line.</summary>
    public static List<LineSpan> LineRanges(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<LineSpan>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            var end = i;
            if (i > start && text[i - 1] == '\r')
            {
                end = i - 1;
            }

            lines.Add(new LineSpan(start, end));
            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(new LineSpan(start, text.Length));
        }

        return lines;
    }

    /// <summary>The narrowest text a wrapped row keeps, however small the window.</summary>
    public const int MinWrapColumns = 10;

    /// <summary>
    /// How many characters of <paramref name="charWidth"/> fit in <paramref name="width"/> (both in
    /// the same unit, pixels for the viewer); 0 when either is not positive.
    /// </summary>
    public static int ColumnsIn(double width, double charWidth)
    {
        if (!(width > 0) || !(charWidth > 0) || double.IsInfinity(width))
        {
            return 0;
        }

        // The epsilon keeps a width that is an exact multiple from losing a column to rounding.
        return (int)Math.Min(int.MaxValue, Math.Floor((width / charWidth) + 1e-6));
    }

    /// <summary>The text width of a wrapped row: the row's columns less its gutter, never under <see cref="MinWrapColumns"/>.</summary>
    public static int WrapWidth(int columns, int gutterColumns) => Math.Max(MinWrapColumns, columns - Math.Max(0, gutterColumns));

    /// <summary>Splits every line into rows of at most <paramref name="columns"/> characters.</summary>
    public static List<VisualRow> WrapRows(string text, IReadOnlyList<LineSpan> lines, int columns)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lines);
        var width = Math.Max(1, columns);
        var rows = new List<VisualRow>(lines.Count);
        for (var line = 0; line < lines.Count; line++)
        {
            AppendWrapped(rows, text, lines[line], line, width);
        }

        return rows;
    }

    /// <summary>The part of a line shown without wrapping. A cut line gains a space and an ellipsis.</summary>
    public static string ClipLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var index = 0;
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count == ViewerLimits.MaxLineChars)
            {
                return string.Concat(text.AsSpan(0, index), " …");
            }

            index += rune.Utf16SequenceLength;
            count++;
        }

        return text;
    }

    /// <summary>
    /// Line index of <paramref name="query"/> at or after <paramref name="from"/>, wrapping around.
    /// <paramref name="forward"/> false walks backwards. An empty query matches nothing.
    /// </summary>
    public static int? FindLine(
        string text,
        IReadOnlyList<LineSpan> lines,
        string query,
        bool matchCase,
        int from,
        bool forward)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(query);
        var count = lines.Count;
        if (count == 0 || query.Length == 0)
        {
            return null;
        }

        var lowered = matchCase ? null : query.ToLowerInvariant();
        var start = Modulo(from, count);
        for (var step = 0; step < count; step++)
        {
            var index = forward ? (start + step) % count : (start + count - step) % count;
            if (LineHits(text, lines[index], query, lowered))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Offset of <paramref name="needle"/> at or after <paramref name="from"/>, wrapping around.
    /// ASCII case folding when <paramref name="matchCase"/> is false.
    /// </summary>
    public static int? FindBytes(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> needle,
        bool matchCase,
        int from,
        bool forward)
    {
        if (needle.Length == 0 || needle.Length > data.Length)
        {
            return null;
        }

        var last = data.Length - needle.Length;
        var count = last + 1;
        var start = from;
        if (start < 0 || start >= count)
        {
            start = forward ? 0 : last;
        }

        for (var step = 0; step < count; step++)
        {
            var index = forward ? (start + step) % count : (start + count - step) % count;
            if (BytesMatch(data, needle, index, matchCase))
            {
                return index;
            }
        }

        return null;
    }

    private static string DecodeBody(ReadOnlySpan<byte> body, ViewerEncoding encoding)
    {
        if (encoding == ViewerEncoding.Utf16Le)
        {
            return DecodeUtf16(body, bigEndian: false);
        }

        if (encoding == ViewerEncoding.Utf16Be)
        {
            return DecodeUtf16(body, bigEndian: true);
        }

        return Encoding.UTF8.GetString(body);
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> body, bool bigEndian)
    {
        var units = body.Length / 2;
        if (units == 0)
        {
            return "";
        }

        var builder = new StringBuilder(units);
        for (var i = 0; i < units; i++)
        {
            var unit = UnitAt(body, i, bigEndian);
            if (char.IsHighSurrogate((char)unit) && i + 1 < units)
            {
                var next = UnitAt(body, i + 1, bigEndian);
                if (char.IsLowSurrogate((char)next))
                {
                    builder.Append((char)unit);
                    builder.Append((char)next);
                    i++;
                    continue;
                }
            }

            if (char.IsSurrogate((char)unit))
            {
                builder.Append('\uFFFD');
                continue;
            }

            builder.Append((char)unit);
        }

        return builder.ToString();
    }

    private static int UnitAt(ReadOnlySpan<byte> body, int index, bool bigEndian)
    {
        var offset = index * 2;
        if (bigEndian)
        {
            return (body[offset] << 8) | body[offset + 1];
        }

        return body[offset] | (body[offset + 1] << 8);
    }

    private static void AppendWrapped(List<VisualRow> rows, string text, LineSpan span, int line, int columns)
    {
        if (!HasExtraRune(text, span, columns))
        {
            rows.Add(new VisualRow(line, span.Start, span.End, true));
            return;
        }

        var start = span.Start;
        var count = 0;
        var first = true;
        var index = span.Start;
        while (index < span.End)
        {
            if (count == columns)
            {
                rows.Add(new VisualRow(line, start, index, first));
                first = false;
                start = index;
                count = 0;
            }

            index += Rune.GetRuneAt(text, index).Utf16SequenceLength;
            count++;
        }

        rows.Add(new VisualRow(line, start, span.End, first));
    }

    private static bool HasExtraRune(string text, LineSpan span, int columns)
    {
        var count = 0;
        var index = span.Start;
        while (index < span.End)
        {
            if (count == columns)
            {
                return true;
            }

            index += Rune.GetRuneAt(text, index).Utf16SequenceLength;
            count++;
        }

        return false;
    }

    private static bool LineHits(string text, LineSpan span, string query, string? lowered)
    {
        var line = text.Substring(span.Start, span.End - span.Start);
        if (lowered is null)
        {
            return line.Contains(query, StringComparison.Ordinal);
        }

        return line.ToLowerInvariant().Contains(lowered, StringComparison.Ordinal);
    }

    private static bool BytesMatch(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle, int index, bool matchCase)
    {
        var window = data.Slice(index, needle.Length);
        if (matchCase)
        {
            return window.SequenceEqual(needle);
        }

        for (var i = 0; i < needle.Length; i++)
        {
            if (AsciiLower(window[i]) != AsciiLower(needle[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte AsciiLower(byte value)
    {
        if (value >= (byte)'A' && value <= (byte)'Z')
        {
            return (byte)(value + 32);
        }

        return value;
    }

    private static int Modulo(int value, int count)
    {
        var rest = value % count;
        if (rest < 0)
        {
            rest += count;
        }

        return rest;
    }

    private static bool HasPrefix(ReadOnlySpan<byte> bytes, byte first, byte second)
    {
        return bytes.Length >= 2 && bytes[0] == first && bytes[1] == second;
    }

    private static bool HasPrefix(ReadOnlySpan<byte> bytes, byte first, byte second, byte third)
    {
        return bytes.Length >= 3 && bytes[0] == first && bytes[1] == second && bytes[2] == third;
    }
}
