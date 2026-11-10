using Avalonia.Threading;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.QuickView;

/// <summary>
/// Ctrl+Q shows the entry under the active panel's cursor in the other panel and follows the cursor
/// (after a short pause, so holding an arrow key does not open every file). Ctrl+Q again, or the
/// other panel getting the focus, brings the file list back. The preview mode (a panel bar button) puts the
/// same view on the panel it was asked on and keeps it there, whichever panel has the focus, until asked again.
/// With a dialog service, a text file opens in the editor: a change is asked about (save, don't save, cancel)
/// before the view moves to another file, goes away, or the app quits.
/// </summary>
public sealed partial class QuickViewCommands : IFileHolder
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(50);

    private readonly PanelWorkspace _panels;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly TimeProvider _clock;
    private readonly IDialogService? _dialogs;
    private DispatcherTimer? _timer;
    private ViewerViewModel? _viewer;
    private QuickViewPanel? _panel;
    private PanelSide _target;
    private PanelSide _source;
    private bool _sticky;
    private string? _wanted;
    private string? _shown;
    private DateTimeOffset _changed;
    private int _ticket;
    private EditorViewModel? _edited;
    private bool _asking;

    public QuickViewCommands(PanelWorkspace panels, ILocalizer text, IJobService jobs, TimeProvider? clock = null, IDialogService? dialogs = null)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        _panels = panels;
        _text = text;
        _jobs = jobs;
        _clock = clock ?? TimeProvider.System;
        _dialogs = dialogs;
    }

    public bool IsOn => _panel is not null;

    /// <summary>The side whose list the quick view covers while it is on.</summary>
    public PanelSide Target => _target;

    internal QuickViewPanel? Panel => _panel;

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.QuickView, Toggle);
        shell.Handle(CommandId.PreviewPanel, TogglePreview);
        shell.GuardQuit(SettleAsync);
    }

    /// <summary>The text being edited in the view; null when it shows anything else.</summary>
    internal EditorViewModel? Edited => _edited;

    /// <summary>
    /// Unsaved changes in the view are asked about: true when there are none, or they were saved or
    /// dropped; false when the user cancelled (or the save failed), and the view must stay.
    /// </summary>
    public async Task<bool> SettleAsync()
    {
        if (_edited is not { Dirty: true } edited || _asking)
        {
            return !_asking;
        }

        _asking = true;
        try
        {
            return await edited.CanCloseAsync().ConfigureAwait(true);
        }
        finally
        {
            _asking = false;
        }
    }

    /// <summary>True while the preview mode holds a panel (it stays when that panel gets the focus).</summary>
    public bool IsSticky => IsOn && _sticky;

    /// <summary>The side whose cursor the view follows while it is on.</summary>
    public PanelSide Source => _source;

    public void Toggle() => _ = ToggleAsync();

    private async Task ToggleAsync()
    {
        if (IsOn)
        {
            if (await SettleAsync().ConfigureAwait(true))
            {
                Stop();
            }

            return;
        }

        Start(Other(_panels.ActiveSide), sticky: false);
    }

    /// <summary>
    /// The active panel turns into a preview of the other panel's cursor, or back into a list when it already
    /// is one; the other panel gets the focus so the arrow keys move what is previewed.
    /// </summary>
    public void TogglePreview() => _ = TogglePreviewAsync();

    private async Task TogglePreviewAsync()
    {
        var side = _panels.ActiveSide;
        var again = IsSticky && _target == side;
        if (IsOn)
        {
            if (!await SettleAsync().ConfigureAwait(true))
            {
                return;
            }

            Stop();
        }

        if (again)
        {
            return;
        }

        Start(side, sticky: true);
        _panels.Activate(_source);
    }

    private static PanelSide Other(PanelSide side) => side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private void Start(PanelSide target, bool sticky)
    {
        _target = target;
        _source = Other(target);
        _sticky = sticky;
        _viewer = new ViewerViewModel(_text, _jobs);
        _panel = new QuickViewPanel(_viewer);
        _wanted = null;
        _shown = null;
        _panels.SetOverlay(_target, _panel);
        _timer = new DispatcherTimer { Interval = Beat };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Follow(true);
    }

    /// <summary>One beat: the focus left the source panel, or the cursor moved and has rested long enough.</summary>
    public void Tick()
    {
        if (!IsOn || _asking)
        {
            return;
        }

        // Typing in the view's editor makes its panel the active one: the quick view stays while it has the focus.
        if (!_sticky && _panels.ActiveSide == _target && _panel?.IsKeyboardFocusWithin != true)
        {
            _ = StopSettledAsync();
            return;
        }

        Follow(false);
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _ticket++;
        if (_panel is not null)
        {
            _panels.SetOverlay(_target, null);
            _panel.Release();
        }

        _viewer?.ReleaseImage();
        _viewer = null;
        _panel = null;
        _edited = null;
        _sticky = false;
    }

    private async Task StopSettledAsync()
    {
        if (await SettleAsync().ConfigureAwait(true))
        {
            Stop();
        }
        else
        {
            _panels.Activate(_source);
        }
    }

    private void Follow(bool immediately)
    {
        var path = UnderCursor();
        if (path != _wanted)
        {
            _wanted = path;
            _changed = _clock.GetUtcNow();
        }

        if (path == _shown && !immediately)
        {
            return;
        }

        if (!immediately && _clock.GetUtcNow() - _changed < Debounce)
        {
            return;
        }

        if (_edited is { } edited && (edited.Dirty || edited.IsBusy) && path != edited.Path)
        {
            _ = LeaveEditedAsync(path);
            return;
        }

        _shown = path;
        if (_edited is not null && path == _edited.Path)
        {
            return;
        }

        Show(path);
    }

    // The cursor left a changed file: settle it first; a cancel keeps the text and waits for the next move.
    private async Task LeaveEditedAsync(string? path)
    {
        var leave = await SettleAsync().ConfigureAwait(true);
        _shown = path;
        if (leave && IsOn)
        {
            Show(path);
        }
    }

    private string? UnderCursor()
    {
        var snap = _panels.Read(_source);
        if (snap.Cursor < 0 || snap.Cursor >= snap.Entries.Count)
        {
            return null;
        }

        var entry = snap.Entries[snap.Cursor];
        return entry.IsParent ? null : entry.Path;
    }

    private void Show(string? path)
    {
        var ticket = ++_ticket;
        _edited = null;
        if (path is null || _panel is null || _viewer is null)
        {
            _panel?.ShowNothing();
            return;
        }

        if (Directory.Exists(path))
        {
            _panel.ShowSummary(Path.GetFileName(path));
            _ = SummarizeAsync(path, ticket);
        }
        else if (File.Exists(path))
        {
            _ = PreviewAsync(path, ticket);
        }
        else
        {
            _panel.ShowNothing();
        }
    }

    /// <summary>
    /// A video of <paramref name="paths"/> playing in the view stops and its file is closed; the view stays empty
    /// on it (the cursor moving on shows the next file).
    /// </summary>
    public async Task LetGoAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var stopped = Task.CompletedTask;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_panel?.ShownVideo is { } video && HeldFiles.Covers(paths, video.FilePath))
            {
                _ticket++;
                _panel.ShowNothing();
                stopped = video.Stopped;
            }
        });
        await stopped.ConfigureAwait(false);
    }

    private async Task SummarizeAsync(string path, int ticket)
    {
        var count = await Task.Run(() => QuickViewSummary.Count(path)).ConfigureAwait(true);
        if (ticket != _ticket || _panel is null)
        {
            return;
        }

        UiThread.Invoke(() => _panel.ShowSummary(QuickViewSummary.Describe(_text, path, count)));
    }
}
