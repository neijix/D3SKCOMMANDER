using System.Text;

namespace D3skCommander.Core.TextDocument;

public sealed partial class TextDocument
{
    /// <summary>Selects the next match of <see cref="Find"/>, wrapping. The view applies <see cref="PendingSelect"/>.</summary>
    public void FindNext()
    {
        var from = 0;
        if (TrySelection(out _, out var end))
        {
            from = end;
        }

        var hit = TextSearch.FindNext(_text, Find, MatchCase, from);
        if (hit is null)
        {
            SetNotice(TextNotice.NotFound, 0, true, "");
            return;
        }

        PendingSelect = hit;
    }

    /// <summary>
    /// Replaces the selection when it is the current match, then selects the next one.
    /// Otherwise it only finds.
    /// </summary>
    public void ReplaceOne()
    {
        if (!TrySelection(out var start, out var end))
        {
            FindNext();
            return;
        }

        var matched = TextSearch.MatchLength(_text, start, Find, MatchCase);
        if (matched != end - start)
        {
            FindNext();
            return;
        }

        _text = TextIndex.Replace(_text, start, end, Replace);
        Dirty = true;
        Selection = null;
        var after = start + Encoding.UTF8.GetByteCount(Replace);
        var next = TextSearch.FindNext(_text, Find, MatchCase, after);
        if (next is null)
        {
            PendingSelect = new Range(after, after);
            SetNotice(TextNotice.NotFound, 0, true, "");
            return;
        }

        PendingSelect = next;
    }

    /// <summary>Replaces every match. A replacement marks the document dirty.</summary>
    public void ReplaceEverywhere()
    {
        var replaced = TextSearch.ReplaceAll(_text, Find, Replace, MatchCase);
        if (replaced.Count > 0)
        {
            _text = replaced.Text;
            Dirty = true;
        }

        SetNotice(TextNotice.Replaced, replaced.Count, false, "");
    }

    /// <summary>Moves <see cref="PendingSelect"/> into <see cref="Selection"/>.</summary>
    public bool ApplyPendingSelect()
    {
        if (PendingSelect is not Range range)
        {
            return false;
        }

        Selection = range;
        PendingSelect = null;
        return true;
    }

    private bool TrySelection(out int start, out int end)
    {
        start = 0;
        end = 0;
        if (Selection is not Range range)
        {
            return false;
        }

        var length = TextIndex.ByteLength(_text);
        start = range.Start.GetOffset(length);
        end = range.End.GetOffset(length);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return true;
    }
}
