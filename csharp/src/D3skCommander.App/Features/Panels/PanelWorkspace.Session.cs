using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Panels;

/// <summary>The session: both panels reopen their saved tabs, sort and view at start, and save them on every change.</summary>
public sealed partial class PanelWorkspace
{
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var home = LocalFileSystem.HomeDirectory();
        if (_settings is null)
        {
            await OpenAsync(home, home, cancellationToken).ConfigureAwait(false);
            return;
        }

        var session = _settings.Session.Current;
        var left = PanelSession.Restore(session.Left, home, Directory.Exists);
        var right = PanelSession.Restore(session.Right, home, Directory.Exists);
        _leftView = ListView(left.View);
        _rightView = ListView(right.View);
        await OpenCore(left, right, cancellationToken).ConfigureAwait(false);
    }

    private static PanelStart Single(string path) =>
        new([path], 0, SortOrder.NameAscending, PanelViewMode.Full);

    private void SaveSession()
    {
        if (_settings is null || _left is null || _right is null)
        {
            return;
        }

        var session = _settings.Session.Current;
        session.Left = PanelSession.Capture(_left, ModeOf(_leftView));
        session.Right = PanelSession.Capture(_right, ModeOf(_rightView));
        _settings.Session.Save(session);
    }

    private static FileListView ListView(PanelViewMode mode)
    {
        if (mode == PanelViewMode.Brief)
        {
            return FileListView.Brief;
        }

        return mode == PanelViewMode.Thumbnails ? FileListView.Thumbs : FileListView.Full;
    }

    private static PanelViewMode ModeOf(FileListView view)
    {
        if (view == FileListView.Brief)
        {
            return PanelViewMode.Brief;
        }

        return view == FileListView.Thumbs ? PanelViewMode.Thumbnails : PanelViewMode.Full;
    }
}
