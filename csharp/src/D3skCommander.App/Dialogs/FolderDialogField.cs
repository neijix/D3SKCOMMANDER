using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace D3skCommander.App.Dialogs;

/// <summary>A folder row. Browse is wired to <see cref="IFolderPicker"/> when the form is shown.</summary>
public sealed class FolderDialogField : DialogField
{
    private string _path;

    public FolderDialogField(string label, string path = "")
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
    }

    public string BrowseText { get; private set; } = "";

    public ICommand? BrowseCommand { get; private set; }

    public string Path
    {
        get => _path;
        set => Set(ref _path, value);
    }

    public void AttachBrowse(string browseText, Func<Task> browse)
    {
        ArgumentException.ThrowIfNullOrEmpty(browseText);
        ArgumentNullException.ThrowIfNull(browse);
        BrowseText = browseText;
        BrowseCommand = new AsyncRelayCommand(browse);
    }
}
