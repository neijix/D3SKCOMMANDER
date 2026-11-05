using System.Globalization;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Viewer;

/// <summary>One text line of a page and the byte offset of its first byte in the file.</summary>
public sealed record ViewerTextLine(long Offset, string Text);

/// <summary>Up to 16 bytes of a hex page and the offset of the first.</summary>
public sealed record ViewerHexRow(long Offset, byte[] Bytes)
{
    /// <summary><see cref="HexRow.Format"/> with an offset that may pass 4 GiB.</summary>
    public string Format() =>
        Offset.ToString("X8", CultureInfo.InvariantCulture) + HexRow.Format(0, Bytes)[8..];
}

/// <summary>
/// One page of a file: the byte range <see cref="Start"/> to <see cref="End"/> (exclusive), and
/// either its text lines or its hex rows, depending on the mode it was read in.
/// </summary>
public sealed record ViewerPage(
    long Start,
    long End,
    bool AtEnd,
    IReadOnlyList<ViewerTextLine> Lines,
    IReadOnlyList<ViewerHexRow> HexRows)
{
    /// <summary>Index of the last line that starts at or before <paramref name="offset"/>; 0 when none does, -1 without lines.</summary>
    public int LineAt(long offset)
    {
        var low = 0;
        var high = Lines.Count - 1;
        var found = Lines.Count == 0 ? -1 : 0;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Lines[middle].Offset <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>Index of the hex row holding <paramref name="offset"/>, clamped to the rows of this page.</summary>
    public int HexRowAt(long offset)
    {
        if (HexRows.Count == 0)
        {
            return -1;
        }

        var row = (offset - Start) / ViewerLimits.HexWidth;
        return (int)Math.Clamp(row, 0, HexRows.Count - 1);
    }
}

/// <summary>A page, or the typed error that stopped the read.</summary>
public readonly record struct ViewerPageResult(ViewerPage? Page, OperationError? Error);

/// <summary>
/// Reads a file one <see cref="ViewerLimits.PageSize"/> page at a time, with no handle kept
/// between calls. A text page starts on a line start and ends after its last complete line, so
/// the next page (<c>exact</c>, at <see cref="ViewerPage.End"/>) continues without a gap; when a
/// page holds no line break it starts and ends on a character boundary instead.
/// </summary>
public static class ViewerPages
{
    private const int ScanChunk = 64 * 1024;

    /// <summary>The encoding the byte order mark at the start of the file announces; UTF-8 when unreadable.</summary>
    public static ViewerEncoding DetectEncoding(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            using var stream = ViewerRead.OpenShared(path);
            var head = new byte[4];
            var count = ViewerRead.ReadAt(stream, 0, head, head.Length);
            return ViewerText.DetectEncoding(head.AsSpan(0, count)).Encoding;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ViewerEncoding.Utf8;
        }
    }

    /// <summary>
    /// Reads the page at <paramref name="offset"/> in text or hex <paramref name="mode"/>.
    /// An offset at or past the end gives the last page. With <paramref name="exact"/> a text
    /// page starts at <paramref name="offset"/> itself (a boundary an earlier page returned).
    /// </summary>
    public static ViewerPageResult Read(
        string path,
        long offset,
        ViewerMode mode,
        ViewerEncoding encoding,
        bool exact = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (mode != ViewerMode.Text && mode != ViewerMode.Hex)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        try
        {
            using var stream = ViewerRead.OpenShared(path);
            var length = stream.Length;
            if (length == 0)
            {
                return new ViewerPageResult(new ViewerPage(0, 0, true, [], []), null);
            }

            var page = mode == ViewerMode.Hex
                ? ReadHex(stream, length, offset)
                : ReadText(stream, length, offset, new TextShape(encoding), exact);
            return new ViewerPageResult(page, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ViewerPageResult(null, OperationError.FromException(ex, path));
        }
    }

    private static ViewerPage ReadHex(Stream stream, long length, long offset)
    {
        var from = offset >= length ? Math.Max(0, length - ViewerLimits.PageSize) : Math.Max(0, offset);
        var start = from / ViewerLimits.HexWidth * ViewerLimits.HexWidth;
        var buffer = new byte[(int)Math.Min(ViewerLimits.PageSize, length - start)];
        var count = ViewerRead.ReadAt(stream, start, buffer, buffer.Length);
        var rows = new List<ViewerHexRow>((count / ViewerLimits.HexWidth) + 1);
        for (var at = 0; at < count; at += ViewerLimits.HexWidth)
        {
            var width = Math.Min(ViewerLimits.HexWidth, count - at);
            rows.Add(new ViewerHexRow(start + at, buffer.AsSpan(at, width).ToArray()));
        }

        var end = start + count;
        return new ViewerPage(start, end, end >= length, [], rows);
    }

    private static ViewerPage ReadText(Stream stream, long length, long offset, TextShape shape, bool exact)
    {
        var start = TextStart(stream, length, offset, shape, exact);
        var buffer = new byte[ViewerLimits.PageSize + 4];
        var count = ViewerRead.ReadAt(stream, start, buffer, buffer.Length);
        var usable = count <= ViewerLimits.PageSize ? count : CutPage(buffer, shape);
        var lines = SplitLines(buffer.AsSpan(0, usable), shape, start);
        var end = start + usable;
        return new ViewerPage(start, end, end >= length, lines, []);
    }

    /// <summary>Where the text page for <paramref name="offset"/> begins.</summary>
    private static long TextStart(Stream stream, long length, long offset, TextShape shape, bool exact)
    {
        if (offset <= 0)
        {
            return 0;
        }

        var wanted = offset;
        var aligned = exact;
        if (wanted >= length)
        {
            wanted = Math.Max(0, length - ViewerLimits.PageSize);
            aligned = false;
            if (wanted == 0)
            {
                return 0;
            }
        }

        if (!aligned)
        {
            var line = NextLineStart(stream, length, wanted, shape);
            if (line is long found && found < length)
            {
                return found;
            }
        }

        return CharacterBoundary(stream, length, wanted, shape);
    }

    /// <summary>The first line start at or after <paramref name="offset"/> within one page of scanning, if any.</summary>
    private static long? NextLineStart(Stream stream, long length, long offset, TextShape shape)
    {
        var unit = shape.Unit;
        var from = ((offset + unit - 1) / unit * unit) - unit;
        var limit = Math.Min(length, from + ViewerLimits.PageSize + unit);
        var buffer = new byte[ScanChunk];
        for (var position = from; position < limit;)
        {
            var count = ViewerRead.ReadAt(stream, position, buffer, (int)Math.Min(ScanChunk, limit - position));
            var span = buffer.AsSpan(0, count);
            for (var i = 0; i + unit <= count; i += unit)
            {
                if (shape.IsNewline(span, i))
                {
                    return position + i + unit;
                }
            }

            var step = count - (count % unit);
            if (step == 0)
            {
                break;
            }

            position += step;
        }

        return null;
    }

    /// <summary><paramref name="offset"/> moved forward to where a character starts.</summary>
    private static long CharacterBoundary(Stream stream, long length, long offset, TextShape shape)
    {
        var head = new byte[4];
        if (shape.Unit == 2)
        {
            var even = (offset + 1) / 2 * 2;
            var read = ViewerRead.ReadAt(stream, even, head, 2);
            if (read == 2 && shape.IsLowSurrogate(head, 0))
            {
                even += 2;
            }

            return Math.Min(even, length);
        }

        var count = ViewerRead.ReadAt(stream, offset, head, head.Length);
        var skip = 0;
        while (skip < count && skip < 3 && (head[skip] & 0xC0) == 0x80)
        {
            skip++;
        }

        return Math.Min(offset + skip, length);
    }

    /// <summary>Bytes of a full buffer that belong to the page: up to the last line break, else to a character boundary.</summary>
    private static int CutPage(byte[] buffer, TextShape shape)
    {
        var cut = ViewerLimits.PageSize;
        if (shape.Unit == 1)
        {
            while (cut > 0 && (buffer[cut] & 0xC0) == 0x80)
            {
                cut--;
            }
        }
        else if (cut >= 2 && shape.IsHighSurrogate(buffer, cut - 2))
        {
            cut -= 2;
        }

        for (var i = cut - shape.Unit; i >= 0; i -= shape.Unit)
        {
            if (shape.IsNewline(buffer.AsSpan(0, cut), i))
            {
                return i + shape.Unit;
            }
        }

        return cut;
    }

    private static List<ViewerTextLine> SplitLines(ReadOnlySpan<byte> data, TextShape shape, long start)
    {
        var lines = new List<ViewerTextLine>();
        var first = start == 0 ? Math.Min(shape.Bom, data.Length) : 0;
        var lineStart = first;
        for (var i = first; i + shape.Unit <= data.Length; i += shape.Unit)
        {
            if (shape.IsNewline(data, i))
            {
                lines.Add(new ViewerTextLine(start + lineStart, shape.Decode(data[lineStart..i])));
                lineStart = i + shape.Unit;
            }
        }

        if (lineStart < data.Length)
        {
            lines.Add(new ViewerTextLine(start + lineStart, shape.Decode(data[lineStart..])));
        }

        return lines;
    }
}

/// <summary>Where a line break sits and how a line decodes, for one <see cref="ViewerEncoding"/>.</summary>
internal readonly struct TextShape
{
    private readonly ViewerEncoding _encoding;

    public TextShape(ViewerEncoding encoding)
    {
        _encoding = encoding;
        Unit = encoding is ViewerEncoding.Utf16Le or ViewerEncoding.Utf16Be ? 2 : 1;
        Bom = encoding switch
        {
            ViewerEncoding.Utf8Bom => 3,
            ViewerEncoding.Utf16Le or ViewerEncoding.Utf16Be => 2,
            _ => 0,
        };
    }

    /// <summary>Bytes per code unit.</summary>
    public int Unit { get; }

    /// <summary>Bytes of the byte order mark at the start of the file.</summary>
    public int Bom { get; }

    public bool IsNewline(ReadOnlySpan<byte> data, int index)
    {
        if (_encoding == ViewerEncoding.Utf16Le)
        {
            return index + 1 < data.Length && data[index] == 0x0A && data[index + 1] == 0;
        }

        if (_encoding == ViewerEncoding.Utf16Be)
        {
            return index + 1 < data.Length && data[index] == 0 && data[index + 1] == 0x0A;
        }

        return data[index] == 0x0A;
    }

    public bool IsHighSurrogate(ReadOnlySpan<byte> data, int index) => UnitAt(data, index) is >= 0xD800 and <= 0xDBFF;

    public bool IsLowSurrogate(ReadOnlySpan<byte> data, int index) => UnitAt(data, index) is >= 0xDC00 and <= 0xDFFF;

    /// <summary>One line without its terminator: a trailing CR dropped, each tab four spaces, bad bytes U+FFFD.</summary>
    public string Decode(ReadOnlySpan<byte> line)
    {
        var text = _encoding switch
        {
            ViewerEncoding.Utf16Le => System.Text.Encoding.Unicode.GetString(line),
            ViewerEncoding.Utf16Be => System.Text.Encoding.BigEndianUnicode.GetString(line),
            _ => System.Text.Encoding.UTF8.GetString(line),
        };
        if (text.EndsWith('\r'))
        {
            text = text[..^1];
        }

        return text.Contains('\t') ? text.Replace("\t", "    ", StringComparison.Ordinal) : text;
    }

    /// <summary>The query as the file stores it, for a byte search.</summary>
    public byte[] Encode(string query) => _encoding switch
    {
        ViewerEncoding.Utf16Le => System.Text.Encoding.Unicode.GetBytes(query),
        ViewerEncoding.Utf16Be => System.Text.Encoding.BigEndianUnicode.GetBytes(query),
        _ => System.Text.Encoding.UTF8.GetBytes(query),
    };

    private int UnitAt(ReadOnlySpan<byte> data, int index)
    {
        if (_encoding == ViewerEncoding.Utf16Le)
        {
            return data[index] | (data[index + 1] << 8);
        }

        if (_encoding == ViewerEncoding.Utf16Be)
        {
            return (data[index] << 8) | data[index + 1];
        }

        return -1;
    }
}

/// <summary>Where a whole-file search found its text: the first byte and the length in bytes.</summary>
public readonly record struct ViewerMatch(long Offset, int Length);

/// <summary>A search's match, none, or the typed error that stopped it.</summary>
public readonly record struct ViewerFound(ViewerMatch? Match, OperationError? Error);

/// <summary>
/// Whole-file search for the paged viewer. It reads the file in chunks (a match across a chunk
/// edge is found), keeps no handle between calls, wraps around the end, and stops at a
/// cancellation. Matching is <see cref="ViewerText.FindBytes"/>: ASCII case folding, the query
/// encoded as the file is (hex mode: UTF-8).
/// </summary>
public static class ViewerSearch
{
    private const int Chunk = 1024 * 1024;

    /// <summary>
    /// First hit starting at or after <paramref name="from"/> (forward) or last hit starting
    /// before it (backward), wrapping once. <paramref name="advanced"/> gets the bytes scanned.
    /// </summary>
    public static ViewerFound Find(
        string path,
        string query,
        bool matchCase,
        ViewerMode mode,
        ViewerEncoding encoding,
        long from,
        bool forward,
        Action<long>? advanced,
        CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            using var stream = ViewerRead.OpenShared(path);
            var shape = new TextShape(mode == ViewerMode.Hex ? ViewerEncoding.Utf8 : encoding);
            var needle = shape.Encode(query);
            if (needle.Length == 0)
            {
                return new ViewerFound(null, null);
            }

            var scan = new Scan(stream, needle, matchCase, mode == ViewerMode.Hex ? 1 : shape.Unit, advanced, cancellation);
            var length = stream.Length;
            var at = Math.Clamp(from, 0, length);
            var offset = forward
                ? scan.Forward(at, length) ?? scan.Forward(0, at)
                : scan.Backward(0, at) ?? scan.Backward(at, length);
            return new ViewerFound(offset is long hit ? new ViewerMatch(hit, needle.Length) : null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ViewerFound(null, OperationError.FromException(ex, path));
        }
    }

    private sealed class Scan(
        Stream stream,
        byte[] needle,
        bool matchCase,
        int unit,
        Action<long>? advanced,
        CancellationToken cancellation)
    {
        private readonly byte[] _buffer = new byte[Chunk + needle.Length - 1];

        /// <summary>First hit starting in [<paramref name="low"/>, <paramref name="high"/>).</summary>
        public long? Forward(long low, long high)
        {
            for (var position = (low + unit - 1) / unit * unit; position < high; position += Chunk)
            {
                cancellation.ThrowIfCancellationRequested();
                var span = Load(position, (int)Math.Min(_buffer.Length, high - position + needle.Length - 1));
                var hit = First(span, position, (int)Math.Min(int.MaxValue, high - position));
                if (hit is long found)
                {
                    return found;
                }

                advanced?.Invoke(Math.Min(Chunk, high - position));
            }

            return null;
        }

        /// <summary>Last hit starting in [<paramref name="low"/>, <paramref name="high"/>).</summary>
        public long? Backward(long low, long high)
        {
            for (var top = high; top > low;)
            {
                cancellation.ThrowIfCancellationRequested();
                var bottom = Math.Max(low, (top - Chunk) / unit * unit);
                var span = Load(bottom, (int)Math.Min(_buffer.Length, top - bottom + needle.Length - 1));
                var hit = Last(span, bottom, (int)(top - bottom));
                if (hit is long found)
                {
                    return found;
                }

                advanced?.Invoke(top - bottom);
                top = bottom;
            }

            return null;
        }

        private ReadOnlySpan<byte> Load(long position, int count) =>
            _buffer.AsSpan(0, ViewerRead.ReadAt(stream, position, _buffer, count));

        private long? First(ReadOnlySpan<byte> data, long position, int starts)
        {
            var index = 0;
            while (index < data.Length - needle.Length + 1 && index < starts)
            {
                var hit = ViewerText.FindBytes(data, needle, matchCase, index, true);
                if (hit is not int found || found < index || found >= starts)
                {
                    return null;
                }

                if ((position + found) % unit == 0)
                {
                    return position + found;
                }

                index = found + 1;
            }

            return null;
        }

        private long? Last(ReadOnlySpan<byte> data, long position, int starts)
        {
            var index = Math.Min(starts, data.Length - needle.Length + 1) - 1;
            while (index >= 0)
            {
                var hit = ViewerText.FindBytes(data, needle, matchCase, index, false);
                if (hit is not int found || found > index)
                {
                    return null;
                }

                if ((position + found) % unit == 0)
                {
                    return position + found;
                }

                index = found - 1;
            }

            return null;
        }
    }
}
