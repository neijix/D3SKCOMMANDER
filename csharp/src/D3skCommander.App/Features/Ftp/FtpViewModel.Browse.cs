using D3skCommander.Core.Ftp;

namespace D3skCommander.App.Features.Ftp;

public sealed partial class FtpViewModel
{
    /// <summary>The folder after login (the site's start folder, else the login folder).</summary>
    private async Task ShowStartFolderAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var pwd = await session.WorkingDirectoryAsync(CancellationToken.None).ConfigureAwait(true);
        if (!pwd.IsSuccess)
        {
            Fail(pwd.Error!);
        }

        await GoToAsync(pwd.IsSuccess ? pwd.Value : "/").ConfigureAwait(true);
    }

    /// <summary>Lists <paramref name="directory"/> and shows it. A failure keeps the folder on screen.</summary>
    public async Task<bool> GoToAsync(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var session = _session;
        if (session is null)
        {
            return false;
        }

        Status = _text.Get("ftp.status.listing");
        var listed = await session.ListAsync(directory, CancellationToken.None).ConfigureAwait(true);
        if (!listed.IsSuccess)
        {
            Status = RemotePath;
            Fail(listed.Error!);
            return false;
        }

        ErrorLine = "";
        RemotePath = directory;
        _rows = FtpRows.Rows(directory, listed.Value);
        _remote.Entries = _rows;
        _remote.CursorIndex = 0;
        Status = string.Format(System.Globalization.CultureInfo.InvariantCulture, _text.Get("ftp.status.entries"), listed.Value.Count);
        return true;
    }

    public Task RefreshAsync() => IsConnected ? GoToAsync(RemotePath) : Task.CompletedTask;

    public Task GoUpAsync()
    {
        var parent = FtpPaths.Parent(RemotePath);
        return IsConnected && parent.Length > 0 && parent != RemotePath ? GoToAsync(parent) : Task.CompletedTask;
    }

    /// <summary>Enter on the row under the cursor: a folder (or the parent row) is entered.</summary>
    public Task OpenCursorAsync()
    {
        var index = _remote.CursorIndex;
        if (index < 0 || index >= _rows.Count || !_rows[index].IsDirectory)
        {
            return Task.CompletedTask;
        }

        return GoToAsync(_rows[index].Path);
    }

    /// <summary>Ends the connection and shows the site list again. Safe when not connected.</summary>
    public async Task DisconnectAsync()
    {
        _connecting?.Cancel();
        var session = _session;
        _session = null;
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(true);
        }

        IsConnected = false;
        RemotePath = "";
        _rows = [];
        _remote.Entries = _rows;
        Status = _text.Get("ftp.status.ready");
    }
}
