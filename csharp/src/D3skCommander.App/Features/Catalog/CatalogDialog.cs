using System.Globalization;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Catalog;

/// <summary>
/// Modal catalog dialog: create a <c>.d3cat</c>, search it without the original folder,
/// and open a hit. The lister window is a later row; opening records the catalog path.
/// </summary>
public sealed partial class CatalogDialog : IShellWidget
{
    private readonly IJobService _jobs;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly FileList _hits;
    private readonly List<CatalogEntry> _shown = [];
    private CatalogHeader? _header;
    private int _busy;
    private string _status = "";

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

    public string? Name => "Catalog";

    public FileList Hits => _hits;

    public string Root { get; set; } = "";

    public string OutputPath { get; set; } = "";

    public string Query { get; set; } = "";

    public string? OpenedPath { get; private set; }

    public bool IsOpen { get; private set; }

    public string Status => _status;

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
        _status = _text.Get("catalog.opened");
        return true;
    }

    public void Close() => IsOpen = false;

    internal bool TryBegin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    internal void End() => Interlocked.Exchange(ref _busy, 0);

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
        _status = string.Format(CultureInfo.InvariantCulture, _text.Get("catalog.shown"), hits.Count);
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
