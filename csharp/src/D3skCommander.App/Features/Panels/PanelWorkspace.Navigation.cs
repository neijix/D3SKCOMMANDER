using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        ReloadBoth(cancellationToken);

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var tab = TabOf(_active);
            var current = tab?.Current;
            if (tab is null || current is null)
            {
                return;
            }

            var next = tab.EnterLocation(current);
            if (next is null)
            {
                return;
            }

            await GoCore(_active, next.Path, linked.Token, announce: true).ConfigureAwait(false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public async Task ParentAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var parent = TabOf(_active)?.ParentLocation();
            if (parent is null)
            {
                return;
            }

            await GoCore(_active, parent.Path, linked.Token, announce: true).ConfigureAwait(false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public Task BackAsync(CancellationToken cancellationToken = default) =>
        HistoryAsync(cancellationToken, back: true);

    public Task ForwardAsync(CancellationToken cancellationToken = default) =>
        HistoryAsync(cancellationToken, back: false);

    public Task HomeAsync(CancellationToken cancellationToken = default) =>
        GoAsync(_active, LocalFileSystem.HomeDirectory(), cancellationToken);

    /// <summary>Opens the Computer view in the tab of <paramref name="side"/> (the command and the drive menu line).</summary>
    public Task ComputerAsync(PanelSide side, CancellationToken cancellationToken = default) =>
        GoAsync(side, ComputerAddress.Text, cancellationToken);

    public async Task RootAsync(CancellationToken cancellationToken = default)
    {
        var path = Read(_active).Path;
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root))
        {
            await GoAsync(_active, root, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SwapAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var left = TabOf(PanelSide.Left)?.Location.Path;
            var right = TabOf(PanelSide.Right)?.Location.Path;
            if (left is null || right is null)
            {
                return;
            }

            await GoCore(PanelSide.Left, right, linked.Token, announce: true).ConfigureAwait(false);
            await GoCore(PanelSide.Right, left, linked.Token, announce: true).ConfigureAwait(false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public async Task SameDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var path = Read(_active).Path;
        if (path.Length > 0)
        {
            await GoAsync(Other(_active), path, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task BrowseAsync(PanelSide side, CancellationToken cancellationToken = default)
    {
        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        Activate(side);
        var start = Read(side).Path;
        var chosen = await _folders.PickAsync(start.Length == 0 ? null : start, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(chosen))
        {
            await GoAsync(side, chosen, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Switch() => _active = Other(_active);

    public void UseView(FileListView view)
    {
        if (!TryEnter())
        {
            return;
        }

        try
        {
            if (_active == PanelSide.Left)
            {
                _leftView = view;
            }
            else
            {
                _rightView = view;
            }

            Publish(_active, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>Sorts the active panel by <paramref name="key"/> (again: the other way) off the UI thread: 100k rows take a while.</summary>
    public async Task SortAsync(SortKey key, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var side = _active;
            var tab = TabOf(side);
            if (tab is null)
            {
                return;
            }

            await Task.Run(() => tab.SetSort(key), linked.Token).ConfigureAwait(false);
            Publish(side, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public void SelectAll() => EditSelection(tab => tab.SelectAll());

    public void UnselectAll() => EditSelection(tab => tab.ClearSelection());

    public void SelectAllButFirst() => EditSelection(tab => tab.SelectAllButFirstOfGroups());

    public void Invert() => EditSelection(tab => tab.InvertSelection());

    public async Task ToggleHiddenAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            _showHidden = !_showHidden;
            SaveShowHidden();
            await Reload(PanelSide.Left, linked.Token).ConfigureAwait(false);
            await Reload(PanelSide.Right, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public async Task NewTabAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var panel = _active == PanelSide.Left ? _left : _right;
            if (panel is null)
            {
                return;
            }

            await Task.Run(
                async () => await panel.NewTabAsync(_showHidden, linked.Token).ConfigureAwait(false),
                linked.Token).ConfigureAwait(false);
            Remember(_active);
            Publish(_active, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public void CloseTab()
    {
        if (!TryEnter())
        {
            return;
        }

        try
        {
            (_active == PanelSide.Left ? _left : _right)?.CloseTab();
            Remember(_active);
            Publish(_active, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public void NextTab()
    {
        if (!TryEnter())
        {
            return;
        }

        try
        {
            (_active == PanelSide.Left ? _left : _right)?.NextTab();
            Publish(_active, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    private async Task<bool> GoCore(PanelSide side, string path, CancellationToken cancellationToken, bool announce)
    {
        var tab = TabOf(side);
        if (tab is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        ClearInput(side);
        string full;
        try
        {
            full = Resolve(tab.Location.Path, path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Fail(side, new OperationError(ErrorKind.InvalidData, ex.Message, path));
            return false;
        }

        if (string.Equals(full, tab.Location.Path, StringComparison.Ordinal))
        {
            return true;
        }

        var ok = await Task.Run(
            async () => await tab.GoAsync(new Location(full), _showHidden, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        if (ok)
        {
            ClearInput(side);
            PreferEntry(tab);
            RememberVisit(full);
        }

        Remember(side);
        Publish(side, announce && !ok);
        return ok;
    }

    private async Task HistoryAsync(CancellationToken cancellationToken, bool back)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var tab = TabOf(_active);
            if (tab is null)
            {
                return;
            }

            var before = tab.Error?.Message;
            var location = tab.Location.Path;
            await Task.Run(
                async () =>
                {
                    if (back)
                    {
                        await tab.BackAsync(_showHidden, linked.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await tab.ForwardAsync(_showHidden, linked.Token).ConfigureAwait(false);
                    }
                },
                linked.Token).ConfigureAwait(false);
            var failed = tab.Error is not null
                && tab.Location.Path == location
                && tab.Error.Message != before;
            if (!failed && tab.Error is null)
            {
                RememberVisit(tab.Location.Path);
            }

            Remember(_active);
            Publish(_active, failed);
        }
        finally
        {
            _nav.Release();
        }
    }

    private async Task ReloadBoth(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await Reload(PanelSide.Left, linked.Token).ConfigureAwait(false);
            await Reload(PanelSide.Right, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _nav.Release();
        }
    }

    private async Task Reload(PanelSide side, CancellationToken cancellationToken)
    {
        var tab = TabOf(side);
        if (tab is null)
        {
            return;
        }

        await Task.Run(
            async () => await tab.ReloadAsync(_showHidden, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        Remember(side);
        Publish(side, announce: false);
    }

    private void EditSelection(Action<Tab> edit)
    {
        if (!TryEnter())
        {
            return;
        }

        try
        {
            var tab = TabOf(_active);
            if (tab is null)
            {
                return;
            }

            edit(tab);
            Publish(_active, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    private static void PreferEntry(Tab tab)
    {
        if (tab.Current is { IsParent: true })
        {
            tab.PlaceCursor(1);
        }
    }

    internal static string Resolve(string current, string typed)
    {
        var text = typed.Trim();
        if (ComputerAddress.IsAddress(text))
        {
            return ComputerAddress.Text;
        }

        if (text == "~")
        {
            return LocalFileSystem.HomeDirectory();
        }

        var home = LocalFileSystem.HomeDirectory();
        if (text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("~\\", StringComparison.Ordinal))
        {
            text = Path.Combine(home, text[2..]);
        }

        if (!Path.IsPathRooted(text))
        {
            // Computer has no folder to join to: a relative name opens under Home.
            text = Path.Combine(ComputerAddress.IsAddressOrUnder(current) ? home : current, text);
        }

        return Path.GetFullPath(text);
    }
}
