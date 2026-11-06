using D3skCommander.App.Shell;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

/// <summary>Favorite tabs: the open tabs of both panels as a set, and a set opened again.</summary>
public sealed partial class PanelWorkspace
{
    /// <summary>The local tabs of both panels as a set called <paramref name="name"/>; null while the panels are not open.</summary>
    public FavoriteTabSet? CaptureFavorites(string name)
    {
        if (_left is null || _right is null || !TryEnter())
        {
            return null;
        }

        try
        {
            var active = _active == PanelSide.Left ? FavoriteSide.Left : FavoriteSide.Right;
            return FavoriteTabsPlan.Capture(name, _left, _right, ModeOf(_leftView), ModeOf(_rightView), active);
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>Opens <paramref name="set"/>: its tabs replace the open ones or come after them. A folder that is gone is skipped.</summary>
    public async Task RestoreFavoritesAsync(FavoriteTabSet set, FavoriteRestoreMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_left is null || _right is null)
            {
                return;
            }

            var plan = FavoriteTabsPlan.Restore(set, mode, _left.Tabs.Count, _right.Tabs.Count, Directory.Exists, LocalFileSystem.HomeDirectory());
            _left = await ApplyAsync(_left, plan.Left, linked.Token).ConfigureAwait(false);
            _right = await ApplyAsync(_right, plan.Right, linked.Token).ConfigureAwait(false);
            if (plan.Left.View is { } leftView)
            {
                _leftView = ListView(leftView);
            }

            if (plan.Right.View is { } rightView)
            {
                _rightView = ListView(rightView);
            }

            _active = plan.ActiveSide == FavoriteSide.Left ? PanelSide.Left : PanelSide.Right;
            PreferEntry(_left.Active);
            PreferEntry(_right.Active);
            Remember(PanelSide.Left);
            Remember(PanelSide.Right);
            Publish(PanelSide.Left, announce: false);
            Publish(PanelSide.Right, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    private async Task<Panel> ApplyAsync(Panel panel, FavoriteSidePlan plan, CancellationToken cancellationToken)
    {
        if (!plan.KeepOpen)
        {
            return await Task.Run(
                async () => await Panel.OpenStartsAsync(plan.Open, plan.Active, _showHidden, _files, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }

        await Task.Run(
            async () => await panel.AddStartsAsync(plan.Open, plan.Active, _showHidden, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        return panel;
    }
}
