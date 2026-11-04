using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Controls.FileList;

public partial class FileList
{
    public bool BeginRename()
    {
        if (_cursorIndex < 0 || _cursorIndex >= _rowList.Count)
        {
            return false;
        }

        var row = _rowList[_cursorIndex];
        if (!row.CanRename)
        {
            return false;
        }

        CloseEditor();
        Editing = row;
        row.EditBuffer = row.EditName;
        row.IsEditing = true;
        Rows.ScrollIntoView(row);
        FocusEditor();
        return true;
    }

    public void CancelRename() => CloseEditor();

    public void CommitRename()
    {
        if (Editing is null)
        {
            return;
        }

        var row = Editing;
        if (LocalFileSystem.ValidateName(row.EditBuffer) is not null)
        {
            return;
        }

        var name = row.EditBuffer;
        CloseEditor();
        RaiseRename(row, name);
    }

    private void OnRenameKey(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void CloseEditor()
    {
        if (Editing is null)
        {
            return;
        }

        Editing.IsEditing = false;
        Editing = null;
        if (VisualRoot is not null)
        {
            Focus();
        }
    }

    private void FocusEditor()
    {
        var view = RowViewAt(_cursorIndex);
        if (view is not null)
        {
            view.FocusEditor();
            return;
        }

        var index = _cursorIndex;
        Dispatcher.UIThread.Post(() => RowViewAt(index)?.FocusEditor(), DispatcherPriority.Loaded);
    }
}
