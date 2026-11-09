namespace D3skCommander.Core.TextDocument;

/// <summary>
/// One change to a text: replace <see cref="Length"/> characters at <see cref="Offset"/> by <see cref="Insert"/>, then
/// select <see cref="SelectLength"/> characters from <see cref="SelectStart"/> (offsets in the changed text).
/// </summary>
public sealed record LineEdit(int Offset, int Length, string Insert, int SelectStart, int SelectLength);

/// <summary>
/// The line commands of the editor's Edit menu, as edits on the text (UTF-16 offsets, any line ending):
/// duplicate, delete or move the caret's lines, and change the case of the selection. Null means nothing to do.
/// </summary>
public static class LineEdits
{
    /// <summary>The selection repeated after itself; with no selection, the caret's line under itself.</summary>
    public static LineEdit? Duplicate(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        (start, length) = Clamp(text, start, length);
        if (length > 0)
        {
            var part = text.Substring(start, length);
            return new LineEdit(start + length, 0, part, start + length, length);
        }

        var (lineStart, lineEnd, _) = LineAt(text, start);
        var line = text[lineStart..lineEnd];
        var newline = NewLineOf(text);
        var column = start - lineStart;
        return new LineEdit(lineEnd, 0, newline + line, lineEnd + newline.Length + column, 0);
    }

    /// <summary>The lines the selection touches, with their line break.</summary>
    public static LineEdit? DeleteLines(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        (start, length) = Clamp(text, start, length);
        var (first, last) = Lines(text, start, length);
        if (text.Length == 0)
        {
            return null;
        }

        var from = first.Start;
        var to = last.Next;
        if (to == last.End && from > 0)
        {
            // The last line has no break after it: take the one before it.
            from = PreviousBreak(text, from);
        }

        return new LineEdit(from, to - from, "", Math.Min(from, text.Length - (to - from)), 0);
    }

    /// <summary>The lines the selection touches swapped with the line above (<paramref name="up"/>) or below.</summary>
    public static LineEdit? MoveLines(string text, int start, int length, bool up)
    {
        ArgumentNullException.ThrowIfNull(text);
        (start, length) = Clamp(text, start, length);
        var (first, last) = Lines(text, start, length);
        if (up ? first.Start == 0 : last.Next == last.End)
        {
            return null;
        }

        var block = text[first.Start..last.End];
        var newline = NewLineOf(text);
        if (up)
        {
            var above = LineAt(text, PreviousBreak(text, first.Start));
            var other = text[above.Start..above.End];
            var replaced = block + newline + other;
            return new LineEdit(above.Start, last.End - above.Start, replaced, start - (first.Start - above.Start), length);
        }

        var below = LineAt(text, last.Next);
        var next = text[below.Start..below.End];
        var moved = next + newline + block;
        var shift = next.Length + newline.Length;
        return new LineEdit(first.Start, below.End - first.Start, moved, start + shift, length);
    }

    /// <summary>The selection in upper case (<paramref name="upper"/>) or lower case; null without a selection.</summary>
    public static LineEdit? ChangeCase(string text, int start, int length, bool upper)
    {
        ArgumentNullException.ThrowIfNull(text);
        (start, length) = Clamp(text, start, length);
        if (length == 0)
        {
            return null;
        }

        var part = text.Substring(start, length);
        var changed = upper ? part.ToUpperInvariant() : part.ToLowerInvariant();
        return changed == part ? null : new LineEdit(start, length, changed, start, changed.Length);
    }

    /// <summary>The text with <paramref name="edit"/> applied.</summary>
    public static string Apply(string text, LineEdit edit)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edit);
        return string.Concat(text.AsSpan(0, edit.Offset), edit.Insert, text.AsSpan(edit.Offset + edit.Length));
    }

    private static (int Start, int Length) Clamp(string text, int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        return (start, Math.Clamp(length, 0, text.Length - start));
    }

    // The first and last line the range touches; a range ending right after a break leaves that next line out.
    private static ((int Start, int End, int Next) First, (int Start, int End, int Next) Last) Lines(string text, int start, int length)
    {
        var first = LineAt(text, start);
        var end = length > 0 && start + length > first.Start ? start + length : start;
        var lastProbe = length > 0 && end > start && (text[end - 1] == '\n' || text[end - 1] == '\r') ? end - 1 : end;
        var last = LineAt(text, Math.Max(start, lastProbe));
        return (first, last);
    }

    // Start and end of the line holding offset (end before its break) and where the next line starts.
    private static (int Start, int End, int Next) LineAt(string text, int offset)
    {
        var start = offset;
        while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r')
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && text[end] != '\n' && text[end] != '\r')
        {
            end++;
        }

        var next = end;
        if (next < text.Length && text[next] == '\r')
        {
            next++;
        }

        if (next < text.Length && text[next] == '\n' && (next == end || text[next - 1] == '\r'))
        {
            next++;
        }

        return (start, end, next);
    }

    // Where the break before the line starting at lineStart begins (lineStart > 0).
    private static int PreviousBreak(string text, int lineStart)
    {
        var at = lineStart - 1;
        if (at > 0 && text[at] == '\n' && text[at - 1] == '\r')
        {
            at--;
        }

        return at;
    }

    private static string NewLineOf(string text)
    {
        var index = text.IndexOf('\n', StringComparison.Ordinal);
        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }
}
