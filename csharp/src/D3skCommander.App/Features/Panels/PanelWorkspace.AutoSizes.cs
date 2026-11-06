using System.Diagnostics;
using D3skCommander.App.Shell;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    private static readonly TimeSpan SizeBatchInterval = TimeSpan.FromMilliseconds(250);

    private readonly AutoSizeRun?[] _autoRuns = new AutoSizeRun?[2];
    private readonly object _autoGate = new();

    /// <summary>Folder sizes measured this session (by Space, Ctrl+L or the automatic setting), valid while a folder's last-write time is unchanged.</summary>
    public FolderSizeCache SizeCache { get; } = new();

    /// <summary>Shows several folder sizes of <paramref name="side"/> at once (one row rebuild) while it still lists <paramref name="directory"/>.</summary>
    public async Task ApplyFolderSizesAsync(
        PanelSide side,
        string directory,
        IReadOnlyList<KeyValuePair<string, ulong>> sizes,
        CancellationToken cancellationToken = default)
    {
        if (sizes.Count == 0)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var tab = TabOf(side);
            if (tab is null || tab.Location.Path != directory)
            {
                return;
            }

            foreach (var (folder, bytes) in sizes)
            {
                tab.SetFolderSize(folder, bytes);
            }

            Publish(side, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    // Called after every publish: starts one background run per listing of a local folder, and only
    // when the listing's folders changed (a size arriving republishes the same folders and must not restart it).
    private void UpdateAutoSizes(PanelSide side, Core.Panels.Tab tab, PanelSnapshot snap)
    {
        var enabled = _settings?.Preferences.Current.AutoFolderSizes ?? false;
        var local = tab.Location.Provider is null && tab.Error is null && Directory.Exists(snap.Path);
        var folders = enabled && local
            ? snap.Entries.Where(e => e.IsDirectory && !e.IsLink && !e.IsParent).Select(e => e.Path).ToList()
            : [];
        var hash = new HashCode();
        hash.Add(snap.Path, StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            hash.Add(folder, StringComparer.Ordinal);
        }

        var key = hash.ToHashCode();
        lock (_autoGate)
        {
            var slot = (int)side;
            var current = _autoRuns[slot];
            if (current is not null && current.Key == key && current.Directory == snap.Path)
            {
                return;
            }

            current?.Cancel.Cancel();
            _autoRuns[slot] = null;
            if (folders.Count == 0)
            {
                return;
            }

            var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _autoRuns[slot] = new AutoSizeRun(snap.Path, key, cancel);
            var token = cancel.Token;
            _ = Task.Run(() => MeasureFoldersAsync(side, snap.Path, folders, token), CancellationToken.None);
        }
    }

    private async Task MeasureFoldersAsync(PanelSide side, string directory, List<string> folders, CancellationToken token)
    {
        try
        {
            var batch = new List<KeyValuePair<string, ulong>>();
            var missing = new List<(string Path, DateTime LastWrite)>();
            foreach (var folder in folders)
            {
                var stamp = Directory.GetLastWriteTimeUtc(folder);
                if (SizeCache.Get(folder, stamp) is { } cached)
                {
                    batch.Add(new(folder, cached));
                }
                else
                {
                    missing.Add((folder, stamp));
                }
            }

            await FlushAsync(side, directory, batch, token).ConfigureAwait(false);
            var since = Stopwatch.StartNew();
            foreach (var (folder, stamp) in missing)
            {
                token.ThrowIfCancellationRequested();
                var size = LocalFileSystem.DirectorySize(folder, token);
                token.ThrowIfCancellationRequested();
                SizeCache.Set(folder, stamp, size.Bytes);
                batch.Add(new(folder, size.Bytes));
                if (since.Elapsed >= SizeBatchInterval)
                {
                    await FlushAsync(side, directory, batch, token).ConfigureAwait(false);
                    since.Restart();
                }
            }

            await FlushAsync(side, directory, batch, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer listing, a changed setting or shutdown replaced this run.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder vanished or became unreadable mid-run: the sizes already shown stay.
        }
    }

    private async Task FlushAsync(PanelSide side, string directory, List<KeyValuePair<string, ulong>> batch, CancellationToken token)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var sizes = batch.ToList();
        batch.Clear();
        await ApplyFolderSizesAsync(side, directory, sizes, token).ConfigureAwait(false);
    }

    private sealed record AutoSizeRun(string Directory, int Key, CancellationTokenSource Cancel);
}
