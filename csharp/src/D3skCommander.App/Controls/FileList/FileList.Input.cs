namespace D3skCommander.App.Controls.FileList;

public sealed partial class FileList
{
    public void PointerDown(int index, FileListModifiers modifiers)
    {
        if (IsRenaming)
        {
            CancelRename();
        }

        if (index < 0 || index >= _rowList.Count)
        {
            return;
        }

        if (modifiers.HasFlag(FileListModifiers.Shift))
        {
            AddRange(_cursorIndex, index);
        }
        else if (modifiers.HasFlag(FileListModifiers.Toggle))
        {
            ToggleKey(_rowList[index]);
        }
        else
        {
            ClearMarks();
        }

        PlaceCursor(index, true);
    }

    /// <summary>A move or a long hold after the press does not change the row (v0.1).</summary>
    public void PointerMove(int index, FileListModifiers modifiers)
    {
        _ = index;
        _ = modifiers;
    }

    public void PointerUp(int index, FileListModifiers modifiers)
    {
        _ = index;
        _ = modifiers;
    }

    public bool Key(FileListKey key, FileListModifiers modifiers = FileListModifiers.None)
    {
        if (IsRenaming)
        {
            return OnRenameKey(key);
        }

        if (modifiers.HasFlag(FileListModifiers.Command))
        {
            return false;
        }

        return ApplyKey(key, modifiers.HasFlag(FileListModifiers.Shift));
    }

    private bool ApplyKey(FileListKey key, bool shift)
    {
        if (key == FileListKey.PageDown)
        {
            Move(FileListNavigation.PageStep, shift);
        }
        else if (key == FileListKey.PageUp)
        {
            Move(-FileListNavigation.PageStep, shift);
        }
        else if (key == FileListKey.Down)
        {
            Move(ThumbsActive ? _thumbColumns : 1, shift);
        }
        else if (key == FileListKey.Up)
        {
            Move(ThumbsActive ? -_thumbColumns : -1, shift);
        }
        else if (key == FileListKey.Right && ThumbsActive)
        {
            Move(1, shift);
        }
        else if (key == FileListKey.Left && ThumbsActive)
        {
            Move(-1, shift);
        }
        else if (key == FileListKey.End)
        {
            Move(_rowList.Count, shift);
        }
        else if (key == FileListKey.Home)
        {
            Move(-_rowList.Count, shift);
        }
        else if (key == FileListKey.Insert)
        {
            MarkInsert();
        }
        else if (key == FileListKey.Space)
        {
            MarkSpace();
        }
        else
        {
            return false;
        }

        return true;
    }

    private void Move(int delta, bool shift)
    {
        if (_rowList.Count == 0)
        {
            return;
        }

        if (!shift)
        {
            PlaceCursor(IndexAfter(delta), true);
            return;
        }

        ShiftMove(delta);
    }

    private void ShiftMove(int delta)
    {
        var steps = Math.Abs((long)delta);
        if (steps > _rowList.Count)
        {
            steps = _rowList.Count;
        }

        if (steps < 1)
        {
            steps = 1;
        }

        var sign = delta < 0 ? -1 : 1;
        for (long n = 0; n < steps; n++)
        {
            var at = _cursorIndex < 0 ? 0 : _cursorIndex;
            ToggleKey(_rowList[at]);
            var before = _cursorIndex;
            PlaceCursor(IndexAfter(sign), false);
            if (_cursorIndex == before)
            {
                break;
            }
        }
    }

    private void MarkInsert()
    {
        if (_cursorIndex < 0 || _cursorIndex >= _rowList.Count)
        {
            return;
        }

        ToggleKey(_rowList[_cursorIndex]);
        Move(1, false);
    }

    private void MarkSpace()
    {
        if (_cursorIndex < 0 || _cursorIndex >= _rowList.Count)
        {
            return;
        }

        var row = _rowList[_cursorIndex];
        if (row.IsDirectory && !row.IsParent && !row.IsMarked)
        {
            ToggleKey(row);
            RaiseDirectoryMarked(row);
            return;
        }

        ToggleKey(row);
    }

    private void PlaceCursor(int index, bool scroll)
    {
        if (_rowList.Count == 0)
        {
            ClearCursor();
            return;
        }

        var clamped = Math.Clamp(index, 0, _rowList.Count - 1);
        if (_cursorIndex != clamped)
        {
            ClearCursorFlag(_cursorIndex);
            _cursorIndex = clamped;
        }

        _rowList[clamped].IsCursor = true;
        if (scroll)
        {
            ScrollTo(clamped);
            CursorRevealed?.Invoke(this, clamped);
        }
    }

    private void ClearCursor()
    {
        ClearCursorFlag(_cursorIndex);
        _cursorIndex = -1;
    }

    private void ClearCursorFlag(int index)
    {
        if (index >= 0 && index < _rowList.Count)
        {
            _rowList[index].IsCursor = false;
        }
    }

    private int IndexAfter(int delta)
    {
        var next = (long)_cursorIndex + delta;
        if (next < 0)
        {
            return 0;
        }

        if (next >= _rowList.Count)
        {
            return _rowList.Count - 1;
        }

        return (int)next;
    }

    private void ToggleKey(FileListRow row)
    {
        if (!row.CanMark)
        {
            return;
        }

        if (_marks.Contains(row.Key))
        {
            _marks.Remove(row.Key);
            row.IsMarked = false;
            return;
        }

        _marks.Add(row.Key);
        row.IsMarked = true;
    }

    private void ClearMarks()
    {
        if (_marks.Count == 0)
        {
            return;
        }

        _marks.Clear();
        foreach (var row in _rowList)
        {
            row.IsMarked = false;
        }
    }

    private void AddRange(int from, int to)
    {
        if (_rowList.Count == 0)
        {
            return;
        }

        var start = Math.Clamp(from, 0, _rowList.Count - 1);
        var end = Math.Clamp(to, 0, _rowList.Count - 1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        for (var i = start; i <= end; i++)
        {
            var row = _rowList[i];
            if (!row.CanMark || !_marks.Add(row.Key))
            {
                continue;
            }

            row.IsMarked = true;
        }
    }

    public void CopyMarks(List<string> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Clear();
        foreach (var key in _marks)
        {
            target.Add(key);
        }
    }

    /// <summary>Marks exactly <paramref name="keys"/>; only the rows whose mark changes are touched.</summary>
    public void SetMarks(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var index = IndexOfKey(key);
            if (index >= 0 && _rowList[index].CanMark)
            {
                wanted.Add(key);
            }
        }

        foreach (var key in _marks)
        {
            var index = IndexOfKey(key);
            if (index >= 0 && !wanted.Contains(key))
            {
                _rowList[index].IsMarked = false;
            }
        }

        _marks.Clear();
        foreach (var key in wanted)
        {
            _marks.Add(key);
            _rowList[IndexOfKey(key)].IsMarked = true;
        }
    }
}
