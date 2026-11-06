using D3skCommander.App.Shell;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    public async Task<bool> OpenResultsAsync(
        PanelSide side,
        string startFolder,
        string title,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var panel = side == PanelSide.Left ? _left : _right;
            if (panel is null)
            {
                return false;
            }

            var opened = await Task.Run(
                async () =>
                {
                    var results = ListFileSystemProvider.Results(startFolder, title, paths);
                    var before = panel.Tabs.Count;
                    await panel.NewTabAtAsync(new Location(results.Directory, results), _showHidden, linked.Token)
                        .ConfigureAwait(false);
                    return panel.Tabs.Count > before;
                },
                linked.Token).ConfigureAwait(false);
            Remember(side);
            Publish(side, announce: false);
            return opened;
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>
    /// Moves the active panel to the folder that holds the entry under the cursor and puts the cursor on it.
    /// In a search tab that is the real folder of the hit; in a plain folder it is the folder already shown.
    /// </summary>
    public async Task GoToContainingFolderAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var side = _active;
            var tab = TabOf(side);
            var current = tab?.Current;
            if (tab is null || current is null || current.IsParent)
            {
                return;
            }

            var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current.Path));
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            if (!string.Equals(folder, tab.Location.Path, StringComparison.Ordinal)
                && !await GoCore(side, folder, linked.Token, announce: true).ConfigureAwait(false))
            {
                return;
            }

            tab.CursorToPath(current.Path);
            Publish(side, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }
}
