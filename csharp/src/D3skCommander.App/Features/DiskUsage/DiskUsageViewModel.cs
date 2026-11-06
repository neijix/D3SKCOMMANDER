using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.DiskUsage;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>
/// The disk usage window: one scan on a worker, then the node on show, its children as rows and the ring around it.
/// Going down and up only changes which node is shown; the tree is never walked again.
/// </summary>
public sealed partial class DiskUsageViewModel : ObservableObject, IShellWidget
{
    private readonly ILocalizer _text;
    private readonly IPanelWorkspace _panels;
    private CancellationTokenSource? _cancel;
    private SizeNode? _root;
    private long _generation;

    [ObservableProperty]
    private SizeNode? _current;

    [ObservableProperty]
    private DiskUsageRow? _selected;

    [ObservableProperty]
    private string _pathText = "";

    [ObservableProperty]
    private string _totalsText = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private IReadOnlyList<RingSegment> _segments = [];

    public DiskUsageViewModel(ILocalizer text, IPanelWorkspace panels)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(panels);
        _text = text;
        _panels = panels;
    }

    public string? Name => "DiskUsage";

    /// <summary>The children of <see cref="Current"/>, biggest first.</summary>
    public ObservableCollection<DiskUsageRow> Rows { get; } = [];

    /// <summary>The folder the scan started in, or null before the first scan.</summary>
    public string? RootPath { get; private set; }

    /// <summary>The finished scan, or null while one runs or after it failed.</summary>
    public SizeNode? Root => _root;

    /// <summary>Completes when the latest scan has finished, been cancelled or failed.</summary>
    public Task ScanTask { get; private set; } = Task.CompletedTask;

    public bool HasStatus => Status.Length > 0;

    public Control CreateContent() => new DiskUsageView(this, _text) { Name = Name };

    /// <summary>Scans <paramref name="folder"/> in the background, replacing what was on show.</summary>
    public Task BeginAsync(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        _cancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var generation = ++_generation;
        RootPath = folder;
        _root = null;
        Current = null;
        Rows.Clear();
        Segments = [];
        CanGoUp = false;
        PathText = folder;
        TotalsText = "";
        IsScanning = true;
        Status = string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.scanning"), "0");
        var progress = new Progress<ScanProgress>(value => OnProgress(generation, value));
        ScanTask = RunAsync(folder, progress, generation, cancel.Token);
        return ScanTask;
    }

    public Task RescanAsync() => RootPath is { } path ? BeginAsync(path) : Task.CompletedTask;

    public void Cancel() => _cancel?.Cancel();

    /// <summary>Shows a folder row's contents. Files and the "smaller files" row stay where they are.</summary>
    public bool Descend(SizeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Kind != SizeNodeKind.Folder || _root is null)
        {
            return false;
        }

        Show(node);
        return true;
    }

    public bool DescendSelected() => Selected is { } row && Descend(row.Node);

    /// <summary>Shows the parent of the node on show, never going above the folder that was scanned.</summary>
    public bool Up()
    {
        if (Current is not { Parent: { } parent } || ReferenceEquals(Current, _root))
        {
            return false;
        }

        var came = Current;
        Show(parent);
        Selected = Rows.FirstOrDefault(row => ReferenceEquals(row.Node, came));
        return true;
    }

    /// <summary>The tooltip of a ring segment: its name, size and share of the node on show.</summary>
    public string Describe(RingSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var name = segment.IsOther
            ? string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.other"), segment.Merged)
            : DiskUsageRow.NameOf(segment.Node!, _text);
        var size = LocalFileSystem.FormatSize((ulong)segment.Bytes);
        var share = SizeShares.PercentText(segment.Bytes, Current?.Bytes ?? 0);
        return string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.tip"), name, size, share);
    }

    public bool ClickSegment(RingSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Node is { } node && Descend(node);
    }

    /// <summary>Moves the active panel to the selected item's folder (else the one on show), cursor on the item.</summary>
    public async Task<bool> ShowInPanelAsync()
    {
        var node = Selected?.Node ?? Current;
        if (node is null)
        {
            return false;
        }

        var path = node.Kind == SizeNodeKind.SmallFiles ? Current?.Path ?? node.Path : node.Path;
        return await _panels.RevealAsync(path).ConfigureAwait(true);
    }

    private async Task RunAsync(string folder, IProgress<ScanProgress> progress, long generation, CancellationToken cancel)
    {
        try
        {
            var tree = await Task.Run(() => DiskUsageScanner.Scan(folder, progress, cancel), cancel)
                .ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            _root = tree;
            Status = "";
            Show(tree);
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation)
            {
                Status = _text.Get("diskusage.cancelled");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (generation == _generation)
            {
                Status = string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.failed"), folder);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsScanning = false;
            }
        }
    }

    private void OnProgress(long generation, ScanProgress value)
    {
        if (generation == _generation && IsScanning)
        {
            Status = string.Format(
                CultureInfo.InvariantCulture,
                _text.Get("diskusage.scanning"),
                LocalFileSystem.FormatGrouped((ulong)value.Files));
        }
    }

    private void Show(SizeNode node)
    {
        Current = node;
        PathText = node.Path;
        CanGoUp = !ReferenceEquals(node, _root) && node.Parent is not null;
        Rows.Clear();
        foreach (var child in node.Children)
        {
            Rows.Add(new DiskUsageRow(child, node, _text));
        }

        Selected = Rows.FirstOrDefault();
        Segments = RingLayout.Build(node);
        TotalsText = Totals(node);
    }

    private string Totals(SizeNode node)
    {
        var parts = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.size"), LocalFileSystem.FormatSize((ulong)node.Bytes)),
            string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.files"), LocalFileSystem.FormatGrouped((ulong)node.Files)),
            string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.folders"), LocalFileSystem.FormatGrouped((ulong)node.Folders)),
        };
        if (node.Unreadable > 0)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.unreadable"), node.Unreadable));
        }

        return string.Join("   ·   ", parts);
    }

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
}
