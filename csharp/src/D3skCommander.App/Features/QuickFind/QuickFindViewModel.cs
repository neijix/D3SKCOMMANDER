using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Index;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.QuickFind;

/// <summary>
/// The quick find window: the query box, the results of the latest query and the state of the index.
/// A query runs on a worker after a short pause; a newer one cancels the older, and a late answer is dropped.
/// </summary>
public sealed partial class QuickFindViewModel : ObservableObject, IShellWidget
{
    private readonly ILocalizer _text;
    private readonly IPanelWorkspace _panels;
    private readonly IndexService _index;
    private CancellationTokenSource? _cancel;
    private long _generation;
    private int _posted;
    private NameIndex? _searched;
    private int _total;
    private string _message = "";

    [ObservableProperty]
    private string _queryText = "";

    [ObservableProperty]
    private IReadOnlyList<QuickFindRow> _rows = [];

    [ObservableProperty]
    private QuickFindRow? _selected;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isBuilding;

    public QuickFindViewModel(ILocalizer text, IPanelWorkspace panels, IndexService index)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(index);
        _text = text;
        _panels = panels;
        _index = index;
        index.Changed += OnIndexChanged;
        RefreshStatus();
    }

    /// <summary>Raised when the window is opened again, so the box takes the focus.</summary>
    public event Action? FocusRequested;

    public string? Name => "QuickFind";

    /// <summary>The pause after the last key before a query runs.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(80);

    /// <summary>Completes when the latest query has been answered or dropped.</summary>
    public Task SearchTask { get; private set; } = Task.CompletedTask;

    /// <summary>All matches of the latest query, of which <see cref="Rows"/> shows the first ones.</summary>
    public int Total => _total;

    public Avalonia.Controls.Control CreateContent() => new QuickFindView(this, _text) { Name = Name };

    /// <summary>Opening the window: reads the saved index, or starts the first build when there is none.</summary>
    public async Task OpenedAsync()
    {
        FocusRequested?.Invoke();
        if (_index.Current is null)
        {
            await _index.LoadAsync().ConfigureAwait(true);
        }

        if (_index.Current is null && !_index.IsBuilding)
        {
            _ = _index.BuildAsync();
        }

        SyncIndex();
    }

    public void RebuildIndex()
    {
        _ = _index.BuildAsync();
        SyncIndex();
    }

    public void CancelBuild() => _index.Cancel();

    public void MoveSelection(int delta)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var at = Selected is null ? -1 : IndexOf(Selected);
        var next = at < 0 ? (delta > 0 ? 0 : Rows.Count - 1) : Math.Clamp(at + delta, 0, Rows.Count - 1);
        Selected = Rows[next];
    }

    /// <summary>Shows the selected hit in the active panel. A file gone since the index was built drops out of the list instead.</summary>
    public async Task<bool> RevealSelectedAsync()
    {
        if (Selected is not { } row)
        {
            return false;
        }

        if (!Exists(row.Path))
        {
            Drop(row);
            return false;
        }

        return await _panels.RevealAsync(row.Path).ConfigureAwait(true);
    }

    /// <summary>Opens the shown hits as a tab of the active panel.</summary>
    public async Task<bool> FeedToPanelAsync()
    {
        if (Rows.Count == 0)
        {
            return false;
        }

        var side = _panels.ActiveSide;
        var title = string.Format(CultureInfo.InvariantCulture, _text.Get("quickfind.tab_title"), QueryText.Trim());
        return await _panels.OpenResultsAsync(side, _panels.Read(side).Path, title, [.. Rows.Select(static row => row.Path)]).ConfigureAwait(true);
    }

    partial void OnQueryTextChanged(string value)
    {
        _message = "";
        Search();
    }

    private static bool Exists(string path) =>
        Path.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private int IndexOf(QuickFindRow row)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (ReferenceEquals(Rows[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    private void Drop(QuickFindRow row)
    {
        var at = IndexOf(row);
        Rows = [.. Rows.Where(other => !ReferenceEquals(other, row))];
        _total = Math.Max(0, _total - 1);
        Selected = Rows.Count == 0 ? null : Rows[Math.Min(Math.Max(at, 0), Rows.Count - 1)];
        _message = string.Format(CultureInfo.InvariantCulture, _text.Get("quickfind.gone"), row.Name);
        RefreshStatus();
    }

    private void Search()
    {
        _cancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var generation = ++_generation;
        SearchTask = RunAsync(QueryText, generation, cancel.Token);
    }

    private async Task RunAsync(string text, long generation, CancellationToken cancel)
    {
        try
        {
            if (Debounce > TimeSpan.Zero)
            {
                await Task.Delay(Debounce, cancel).ConfigureAwait(true);
            }

            var index = _index.Current;
            var query = NameQueryParser.Parse(text);
            if (index is null || query.IsEmpty)
            {
                Show(generation, index, IndexSearchResult.None);
                return;
            }

            var result = await Task.Run(() => index.Search(query, NameIndex.DefaultLimit, cancel), cancel).ConfigureAwait(true);
            Show(generation, index, result);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Show(long generation, NameIndex? index, IndexSearchResult result)
    {
        if (generation != _generation)
        {
            return;
        }

        _searched = index;
        _total = result.Total;
        Rows = [.. result.Hits.Select(static hit => new QuickFindRow(hit))];
        Selected = Rows.Count > 0 ? Rows[0] : null;
        RefreshStatus();
    }

    // The index raises Changed from its worker, often: one posted refresh at a time.
    private void OnIndexChanged()
    {
        if (Interlocked.Exchange(ref _posted, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _posted, 0);
                SyncIndex();
            });
        }
    }

    private void SyncIndex()
    {
        IsBuilding = _index.IsBuilding;
        if (!ReferenceEquals(_searched, _index.Current) && QueryText.Trim().Length > 0)
        {
            Search();
        }

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        Status = Compose();
    }

    private string Compose()
    {
        if (_index.IsBuilding)
        {
            return string.Format(CultureInfo.InvariantCulture, _text.Get("quickfind.building"), _index.Progress);
        }

        if (_message.Length > 0)
        {
            return _message;
        }

        if (_index.Current is not { } index)
        {
            return _text.Get(_index.WasCancelled ? "quickfind.build_cancelled" : "quickfind.no_index");
        }

        var built = LocalFileSystem.FormatTime(index.BuiltAt);
        return QueryText.Trim().Length == 0
            ? string.Format(CultureInfo.InvariantCulture, _text.Get("quickfind.status_idle"), built, index.Count)
            : string.Format(CultureInfo.InvariantCulture, _text.Get("quickfind.status"), Rows.Count, _total, built, index.Count);
    }
}
