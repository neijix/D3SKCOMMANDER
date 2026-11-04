using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Controls.FileList;

public sealed class FileListRenameEventArgs : EventArgs
{
    public FileListRenameEventArgs(FileListRow row, string name)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(name);
        Row = row;
        Name = name;
    }

    public FileListRow Row { get; }

    public string Name { get; }

    public Entry? Entry => Row.Source as Entry;
}
