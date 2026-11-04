using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace D3skCommander.App.Controls.FileList;

public partial class FileList
{
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || !IsLeftPress(e) || IsEditor(e.Source))
        {
            return;
        }

        var container = Rows.ContainerFromSource(e.Source);
        if (container is null)
        {
            return;
        }

        var index = Rows.IndexFromContainer(container);
        if (index < 0)
        {
            return;
        }

        // Select on press. A release after a move or a long hold is still this row (v0.1).
        Press(index, e.KeyModifiers);
        e.Handled = true;
    }

    private void Press(int index, KeyModifiers modifiers)
    {
        if (IsRenaming)
        {
            CancelRename();
        }

        if (index < 0 || index >= _rowList.Count)
        {
            return;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            AddRange(_cursorIndex, index);
        }
        else if (FileListNavigation.IsToggle(modifiers))
        {
            ToggleKey(_rowList[index]);
        }
        else
        {
            ClearMarks();
        }

        PlaceCursor(index, true);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (IsRenaming)
        {
            OnRenameKey(e);
            return;
        }

        if (FileListNavigation.IsCommandChord(e.KeyModifiers))
        {
            return;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!ApplyKey(e.Key, shift))
        {
            return;
        }

        e.Handled = true;
    }

    private bool ApplyKey(Key key, bool shift)
    {
        if (key == Key.PageDown)
        {
            Move(FileListNavigation.PageStep, shift);
        }
        else if (key == Key.PageUp)
        {
            Move(-FileListNavigation.PageStep, shift);
        }
        else if (key == Key.Down)
        {
            Move(1, shift);
        }
        else if (key == Key.Up)
        {
            Move(-1, shift);
        }
        else if (key == Key.End)
        {
            Move(_rowList.Count, shift);
        }
        else if (key == Key.Home)
        {
            Move(-_rowList.Count, shift);
        }
        else if (key == Key.Insert)
        {
            MarkInsert();
        }
        else if (key == Key.Space)
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

        if (_cursorIndex >= 0)
        {
            Rows.ScrollIntoView(_rowList[_cursorIndex]);
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
            SetAndRaise(CursorIndexProperty, ref _cursorIndex, clamped);
        }

        _rowList[clamped].IsCursor = true;
        Rows.SelectedIndex = clamped;
        if (scroll)
        {
            Rows.ScrollIntoView(_rowList[clamped]);
        }
    }

    private void ClearCursor()
    {
        ClearCursorFlag(_cursorIndex);
        if (_cursorIndex != -1)
        {
            SetAndRaise(CursorIndexProperty, ref _cursorIndex, -1);
        }

        Rows.SelectedIndex = -1;
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

    private static bool IsLeftPress(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(null).Properties;
        return props.IsLeftButtonPressed || props.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;
    }

    private static bool IsEditor(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is TextBox)
            {
                return true;
            }

            if (current is ListBoxItem)
            {
                return false;
            }
        }

        return false;
    }
}
