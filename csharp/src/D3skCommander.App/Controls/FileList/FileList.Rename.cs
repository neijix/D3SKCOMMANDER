using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Controls.FileList;

public sealed partial class FileList
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
        ScrollTo(_cursorIndex);
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

    private bool OnRenameKey(FileListKey key)
    {
        if (key == FileListKey.Enter)
        {
            CommitRename();
            return true;
        }

        if (key == FileListKey.Escape)
        {
            CancelRename();
            return true;
        }

        return true;
    }

    private void CloseEditor()
    {
        if (Editing is null)
        {
            return;
        }

        Editing.IsEditing = false;
        Editing = null;
    }
}
