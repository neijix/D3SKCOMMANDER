using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Features.Panels;

/// <summary>Left and right panels. Listing runs off the UI thread; a pane reads a snapshot on each beat.</summary>
public sealed partial class PanelWorkspace : IPanelWorkspace, IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1500);

    private readonly IFileSystemProvider _files;
    private readonly IFolderPicker _folders;
    private readonly TimeProvider _clock;
    private readonly AppSettings? _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _nav = new(1, 1);
    private readonly long[] _stamps = new long[2];
    private Panel? _left;
    private Panel? _right;
    private PanelSnapshot _leftSnap = PanelSnapshot.Empty;
    private PanelSnapshot _rightSnap = PanelSnapshot.Empty;
    private FileListView _leftView = FileListView.Full;
    private FileListView _rightView = FileListView.Full;
    private PanelSide _active = PanelSide.Left;
    private OperationError? _inputError;
    private PanelSide _inputSide;
    private bool _showHidden;
    private int _leftGeneration;
    private int _rightGeneration;
    private int _leftFailure;
    private int _rightFailure;
    private DateTimeOffset _polled;
    private int _polling;
    private Task? _opening;

    public PanelWorkspace(
        IFileSystemProvider files,
        IFolderPicker folders,
        TimeProvider? clock = null,
        AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(folders);
        _files = files;
        _folders = folders;
        _clock = clock ?? TimeProvider.System;
        _settings = settings;
        _showHidden = settings?.Preferences.Current.ShowHidden ?? false;
        _showDriveBar = settings?.Preferences.Current.ShowDriveBar ?? true;
        LoadTree(settings?.Preferences.Current);
    }

    public PanelSide ActiveSide => _active;

    public void Dispose() => _lifetime.Cancel();

    public PanelSnapshot Read(PanelSide side) =>
        side == PanelSide.Left ? Volatile.Read(ref _leftSnap) : Volatile.Read(ref _rightSnap);

    public Task EnsureOpen(CancellationToken cancellationToken = default)
    {
        if (_left is not null && _right is not null)
        {
            return Task.CompletedTask;
        }

        return _opening ??= RestoreAsync(cancellationToken);
    }

    public async Task OpenAsync(string left, string right, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(left);
        ArgumentException.ThrowIfNullOrEmpty(right);
        await OpenCore(Single(left), Single(right), cancellationToken).ConfigureAwait(false);
    }

    private async Task OpenCore(PanelStart left, PanelStart right, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            _left = await ListPanel(left, linked.Token).ConfigureAwait(false);
            _right = await ListPanel(right, linked.Token).ConfigureAwait(false);
            PreferEntry(_left.Active);
            PreferEntry(_right.Active);
            Remember(PanelSide.Left);
            Remember(PanelSide.Right);
            _polled = _clock.GetUtcNow();
            Publish(PanelSide.Left, _left.Active.Error is not null);
            Publish(PanelSide.Right, _right.Active.Error is not null);
        }
        finally
        {
            _nav.Release();
        }
    }

    public async Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default)
    {
        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await GoCore(side, path, linked.Token, announce: true).ConfigureAwait(false);
        }
        finally
        {
            _nav.Release();
        }
    }

    public void Activate(PanelSide side) => _active = side;

    public void ApplySelection(PanelSide side, int cursor, IReadOnlyList<string> marked)
    {
        ArgumentNullException.ThrowIfNull(marked);
        if (!TryEnter())
        {
            return;
        }

        try
        {
            var tab = TabOf(side);
            if (tab is null)
            {
                return;
            }

            tab.PlaceCursor(cursor);
            tab.ReplaceSelection(marked);
            // A click must not recapture the listing: that copies every entry and the next frame rebuilds every row.
            PatchSelection(side, tab.Cursor, marked);
        }
        finally
        {
            _nav.Release();
        }
    }

    public void KickOpen() => _ = OpenQuiet();

    public PanelCapture? Capture()
    {
        if (_left is null || _right is null || !TryEnter())
        {
            return null;
        }

        try
        {
            var active = TabOf(_active);
            var other = TabOf(Other(_active));
            if (active is null || other is null)
            {
                return null;
            }

            var sources = new List<string>();
            foreach (var entry in active.Targets())
            {
                sources.Add(entry.Path);
            }

            return new PanelCapture(sources, active.Location.Path, other.Location.Path);
        }
        finally
        {
            _nav.Release();
        }
    }

    private async Task OpenQuiet()
    {
        try
        {
            await EnsureOpen(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!TryEnter())
            {
                return;
            }

            try
            {
                Fail(PanelSide.Left, OperationError.FromException(ex, TabOf(PanelSide.Left)?.Location.Path));
            }
            finally
            {
                _nav.Release();
            }
        }
    }

    public void PollWhenDue()
    {
        if (_left is null || _clock.GetUtcNow() - _polled < RefreshInterval)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
        {
            return;
        }

        _ = PollQuiet();
    }

    private async Task PollQuiet()
    {
        try
        {
            await PollAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        if (_left is null)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        if (now - _polled < RefreshInterval)
        {
            return;
        }

        _polled = now;
        await RefreshSide(PanelSide.Left, cancellationToken).ConfigureAwait(false);
        await RefreshSide(PanelSide.Right, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshSide(PanelSide side, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var watched = await ReadWatch(side, linked.Token).ConfigureAwait(false);
        if (watched is null)
        {
            return;
        }

        if (!PanelWatch.TryRead(watched.Value.Path, out var stamp) || stamp == watched.Value.Known)
        {
            return;
        }

        await ReloadIfUnchanged(side, watched.Value.Path, watched.Value.Known, linked.Token).ConfigureAwait(false);
    }

    private async Task<(string Path, long Known)?> ReadWatch(PanelSide side, CancellationToken cancellationToken)
    {
        await _nav.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tab = TabOf(side);
            if (tab is null)
            {
                return null;
            }

            return (tab.Location.Path, _stamps[Index(side)]);
        }
        finally
        {
            _nav.Release();
        }
    }

    private async Task ReloadIfUnchanged(PanelSide side, string path, long known, CancellationToken cancellationToken)
    {
        await _nav.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tab = TabOf(side);
            if (tab is null || tab.Location.Path != path || _stamps[Index(side)] != known)
            {
                return;
            }

            await Reload(side, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _nav.Release();
        }
    }

    private void PatchSelection(PanelSide side, int cursor, IReadOnlyList<string> marked)
    {
        var copy = new List<string>(marked.Count);
        foreach (var path in marked)
        {
            if (!string.IsNullOrEmpty(path))
            {
                copy.Add(path);
            }
        }

        var next = Read(side) with
        {
            Cursor = cursor,
            Selected = copy.Count,
            Marked = copy,
        };
        if (side == PanelSide.Left)
        {
            Volatile.Write(ref _leftSnap, next);
        }
        else
        {
            Volatile.Write(ref _rightSnap, next);
        }
    }

    private async Task<Panel> ListPanel(PanelStart start, CancellationToken cancellationToken) =>
        await Task.Run(
            async () => await Panel.OpenAsync(
                start.Paths,
                start.Active,
                start.Sort,
                _showHidden,
                _files,
                cancellationToken,
                start.ColumnSets).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    private bool TryEnter() => !_lifetime.IsCancellationRequested && _nav.Wait(0);

    private static PanelSide Other(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private static int Index(PanelSide side) => side == PanelSide.Left ? 0 : 1;

    private Tab? TabOf(PanelSide side)
    {
        var panel = side == PanelSide.Left ? _left : _right;
        return panel?.Active;
    }

    private FileListView ViewOf(PanelSide side) => side == PanelSide.Left ? _leftView : _rightView;

    private void Remember(PanelSide side)
    {
        var tab = TabOf(side);
        if (tab is not null && PanelWatch.TryRead(tab.Location.Path, out var stamp))
        {
            _stamps[Index(side)] = stamp;
        }
    }

    private void Publish(PanelSide side, bool announce)
    {
        var tab = TabOf(side);
        if (tab is null)
        {
            return;
        }

        var failed = tab.Error is not null || (_inputError is not null && _inputSide == side);
        if (announce && failed)
        {
            if (side == PanelSide.Left)
            {
                _leftFailure++;
            }
            else
            {
                _rightFailure++;
            }
        }

        var token = side == PanelSide.Left ? _leftFailure : _rightFailure;
        var generation = side == PanelSide.Left ? ++_leftGeneration : ++_rightGeneration;
        var previous = side == PanelSide.Left ? Volatile.Read(ref _leftSnap) : Volatile.Read(ref _rightSnap);
        var snap = PanelSnapshot.Capture(tab, ViewOf(side), generation, token, previous);
        if (_inputError is not null && _inputSide == side)
        {
            snap = snap with { Error = _inputError.Kind, ErrorPath = _inputError.Path };
        }

        if (side == PanelSide.Left)
        {
            Volatile.Write(ref _leftSnap, snap);
        }
        else
        {
            Volatile.Write(ref _rightSnap, snap);
        }

        SaveSession();
        UpdateAutoSizes(side, tab, snap);
    }

    /// <summary>Shows <paramref name="error"/> on <paramref name="side"/>: its localized kind and path, never the raw exception text.</summary>
    private void Fail(PanelSide side, OperationError error)
    {
        _inputError = error;
        _inputSide = side;
        Publish(side, announce: true);
    }

    private void ClearInput(PanelSide side)
    {
        if (_inputSide == side)
        {
            _inputError = null;
        }
    }
}
