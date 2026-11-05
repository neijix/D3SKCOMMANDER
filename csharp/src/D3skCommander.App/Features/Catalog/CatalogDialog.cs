using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Host;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Catalog;

/// <summary>
/// Model of the catalog window: create a <c>.d3cat</c>, search it without the original folder,
/// and open a hit. The lister window is a later row; opening records the catalog path.
/// </summary>
public sealed partial class CatalogDialog : ObservableObject
{
    private readonly IJobService _jobs;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly FileList _hits;
    private readonly List<CatalogEntry> _shown = [];
    private CatalogHeader? _header;
    private int _busy;
    private string _status = "";
    private string? _openedPath;
    private bool _isOpen;
    [ObservableProperty]
    private string _root = "";
    [ObservableProperty]
    private string _outputPath = "";
    [ObservableProperty]
    private string _query = "";

    public CatalogDialog(IJobService jobs, IDialogService dialogs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _jobs = jobs;
        _dialogs = dialogs;
        _text = text;
        _hits = new FileList(text);
    }

    public FileList Hits => _hits;

    public string? OpenedPath
    {
        get => _openedPath;
        private set => SetProperty(ref _openedPath, value);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public void Prepare(string root, string outputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        Root = root;
        OutputPath = outputPath;
    }

    public async Task<bool> CreateAsync()
    {
        if (!TryBegin())
        {
            return false;
        }

        try
        {
            return await WriteAsync().ConfigureAwait(false);
        }
        finally
        {
            End();
        }
    }

    public Task<bool> SearchAsync() => LoadAsync(Query);

    public bool OpenSelected()
    {
        var header = _header;
        var index = _hits.CursorIndex;
        if (header is null || index < 0 || index >= _shown.Count)
        {
            return false;
        }

        OpenedPath = CatalogHits.Absolute(header, _shown[index]);
        Status = _text.Get("catalog.opened");
        return true;
    }

    public void Close() => IsOpen = false;

    internal bool TryBegin()
    {
        var began = Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
        if (began)
        {
            UiThread.Post(() => OnPropertyChanged(nameof(IsBusy)));
        }

        return began;
    }

    internal void End()
    {
        Interlocked.Exchange(ref _busy, 0);
        UiThread.Post(() => OnPropertyChanged(nameof(IsBusy)));
    }

    internal async Task<bool> WriteAsync()
    {
        var root = Root.Trim();
        var output = OutputPath.Trim();
        if (root.Length == 0 || output.Length == 0)
        {
            return false;
        }

        Root = root;
        OutputPath = output;
        var job = DiskCatalog.Start(_jobs, _text.Get("cmd.catalog"), root, output);
        var result = await job.Completion.ConfigureAwait(false);
        if (!Succeeded(result))
        {
            await Report(Failure(result)).ConfigureAwait(false);
            return false;
        }

        Query = "";
        return await LoadAsync(Query).ConfigureAwait(false);
    }

    private async Task<bool> LoadAsync(string query)
    {
        var path = OutputPath.Trim();
        if (path.Length == 0)
        {
            await Report(OperationError.NotFound(".")).ConfigureAwait(false);
            return false;
        }

        var loaded = await Task.Run(() => CatalogHits.Load(path, query)).ConfigureAwait(false);
        if (loaded.Error is not null)
        {
            await Report(loaded.Error).ConfigureAwait(false);
            return false;
        }

        var document = loaded.Document;
        var hits = loaded.Hits;
        if (document is null || hits is null)
        {
            await Report(OperationError.InvalidData("not a catalog", path)).ConfigureAwait(false);
            return false;
        }

        UiThread.Invoke(() => Show(document, hits));
        return true;
    }

    private void Show(CatalogDocument document, IReadOnlyList<CatalogEntry> hits)
    {
        _header = document.Header;
        _shown.Clear();
        var rows = new List<FileListRow>(hits.Count);
        foreach (var entry in hits)
        {
            _shown.Add(entry);
            rows.Add(CatalogHits.Row(entry, _text));
        }

        _hits.ShowRows(CatalogHits.Columns, rows);
        Status = string.Format(CultureInfo.InvariantCulture, _text.Get("catalog.shown"), hits.Count);
        IsOpen = true;
        OpenedPath = null;
    }

    private Task Report(OperationError error)
    {
        var body = _text.Get(ErrorText.Key(error.Kind));
        if (error.Kind == ErrorKind.InvalidData)
        {
            body = _text.Get("catalog.not_catalog") + "\n" + body;
        }
        else if (error.Message.Length > 0)
        {
            body = body + "\n" + error.Message;
        }

        return _dialogs.ShowMessageAsync(_text.Get("cmd.catalog"), body);
    }

    private OperationError Failure(JobResult result)
    {
        if (result.Failed.Count > 0)
        {
            return result.Failed[0].Error;
        }

        return OperationError.Cancelled(OutputPath);
    }

    private static bool Succeeded(JobResult result) =>
        result.Outcome == JobOutcome.Completed && result.Failed.Count == 0;
}
