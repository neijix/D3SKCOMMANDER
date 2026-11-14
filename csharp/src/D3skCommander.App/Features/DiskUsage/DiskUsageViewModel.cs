using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.DiskUsage;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>
/// The disk usage window: one scan on a worker, then the node on show, its children as rows and the ring around it.
/// Going down and up only changes which node is shown; the tree is never walked again.
/// </summary>
public sealed partial class DiskUsageViewModel : ObservableObject, IShellWidget
{
    private readonly ILocalizer _text;
    private readonly IPanelWorkspace _panels;
    private readonly IPathDeleter? _deleter;
    private readonly Dictionary<string, ScanEstimate> _lastScans = new(StringComparer.Ordinal);
    private ScanEstimate _estimate;
    private CancellationTokenSource? _cancel;
    private SizeNode? _root;
    private long _generation;
    private IReadOnlyList<string>? _drives;
    private IReadOnlyList<string> _skip = [];
    private string _rootName = "";
    private bool _drivesEstimate;

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

    /// <summary>How far the scan is (0 to 99) when <see cref="HasEstimate"/>; the bar runs without an end otherwise.</summary>
    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _hasEstimate;

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private IReadOnlyList<RingSegment> _segments = [];

    /// <param name="deleter">What the list's Delete runs; null: the list offers no delete.</param>
    public DiskUsageViewModel(ILocalizer text, IPanelWorkspace panels, IPathDeleter? deleter = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(panels);
        _text = text;
        _panels = panels;
        _deleter = deleter;
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
        _drives = null;
        return Start(folder);
    }

    /// <summary>
    /// Scans every drive under one root named with the Computer title at <see cref="ComputerAddress.Text"/>, leaving
    /// out <paramref name="skip"/> (the OS mount points). Rescan repeats it.
    /// </summary>
    public Task BeginDrivesAsync(IReadOnlyList<string> drives, IReadOnlyList<string> skip)
    {
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(skip);
        _drives = drives;
        _skip = skip;
        _rootName = _text.Get("computer.title");
        _drivesEstimate = true;
        return Start(ComputerAddress.Text);
    }

    /// <summary>
    /// Scans every share of a network server (<c>\\server\</c>, which has no folder of its own) under one root named
    /// after the server. The shares may sit on one volume: the progress has no estimate. Rescan repeats it.
    /// </summary>
    public Task BeginSharesAsync(string server, IReadOnlyList<string> shares)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        ArgumentNullException.ThrowIfNull(shares);
        _drives = shares;
        _skip = [];
        _rootName = server;
        _drivesEstimate = false;
        return Start(server);
    }

    private Task Start(string folder)
    {
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
        _estimate = ScanEstimate.None;
        HasEstimate = false;
        ProgressPercent = 0;
        Status = string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.scanning"), "0");
        var progress = new Progress<ScanProgress>(value => OnProgress(generation, value));
        ScanTask = RunAsync(folder, progress, generation, cancel.Token);
        _ = EstimateAsync(folder, _drivesEstimate ? _drives : null, generation, cancel.Token);
        return ScanTask;
    }

    public Task RescanAsync() => RootPath is { } path ? Start(path) : Task.CompletedTask;

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
        if (ComputerAddress.IsAddress(path) || UncPath.Server(path) is not null)
        {
            return await _panels.GoAsync(_panels.ActiveSide, path).ConfigureAwait(true);
        }

        return await _panels.RevealAsync(path).ConfigureAwait(true);
    }

    /// <summary>Whether the list's Delete can run on the selected row: a real file or folder, not a drive of an all-drives scan.</summary>
    public bool CanDeleteSelected => _deleter is not null && !IsScanning && DeletablePath(Selected?.Node) is not null;

    /// <summary>
    /// Deletes the selected row's file or folder through the usual Delete (trash and confirmation follow the
    /// preferences), then drops it from the tree on show without scanning again.
    /// </summary>
    public async Task<bool> DeleteSelectedAsync()
    {
        if (_deleter is null || IsScanning || Selected is not { } row || DeletablePath(row.Node) is not { } path)
        {
            return false;
        }

        var index = Rows.IndexOf(row);
        var root = _root;
        if (!await _deleter.DeletePathsAsync([path]).ConfigureAwait(true))
        {
            return false;
        }

        // Cancelled, failed, or a rescan replaced the tree meanwhile: leave the list as it is.
        if (File.Exists(path) || Directory.Exists(path) || root is null || !ReferenceEquals(root, _root) || !IsUnder(row.Node, root))
        {
            return false;
        }

        var (rebuilt, parent) = SizeTree.Remove(root, row.Node);
        _root = rebuilt;
        if (RootPath is { } scanned)
        {
            _lastScans[scanned] = new ScanEstimate(rebuilt.Files, rebuilt.Bytes);
        }

        Show(parent);
        Selected = Rows.Count == 0 ? null : Rows[Math.Clamp(index, 0, Rows.Count - 1)];
        return true;
    }

    private string? DeletablePath(SizeNode? node)
    {
        if (node is null || _root is null || node.Kind == SizeNodeKind.SmallFiles || node.Parent is null
            || ComputerAddress.IsAddress(node.Path))
        {
            return null;
        }

        // An all-drives scan lists the drives themselves under its root: never offer to delete a drive.
        return _drives is not null && ReferenceEquals(node.Parent, _root) ? null : node.Path;
    }

    private static bool IsUnder(SizeNode node, SizeNode root)
    {
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, root))
            {
                return true;
            }
        }

        return false;
    }

    partial void OnSelectedChanged(DiskUsageRow? value) => OnPropertyChanged(nameof(CanDeleteSelected));

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(CanDeleteSelected));

    private async Task RunAsync(string folder, IProgress<ScanProgress> progress, long generation, CancellationToken cancel)
    {
        try
        {
            var drives = _drives;
            var skip = _skip;
            var name = _rootName;
            var tree = await Task.Run(
                    () => drives is null
                        ? DiskUsageScanner.Scan(folder, progress, cancel)
                        : DiskUsageScanner.ScanDrives(name, folder, drives, skip, progress, cancel),
                    cancel)
                .ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            _root = tree;
            _lastScans[folder] = new ScanEstimate(tree.Files, tree.Bytes);
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

    // Before the scan's own count is known: the last scan of the same folder, else the used space of the drive(s).
    private async Task EstimateAsync(string folder, IReadOnlyList<string>? drives, long generation, CancellationToken cancel)
    {
        try
        {
            var estimate = _lastScans.TryGetValue(folder, out var last)
                ? last
                : await Task.Run(() => ScanEstimate.FromDrives(folder, drives, UsedBytes), cancel).ConfigureAwait(true);
            if (generation == _generation && IsScanning && estimate.IsKnown)
            {
                _estimate = estimate;
                HasEstimate = true;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer scan or Cancel: the bar stays without an end.
        }
    }

    private static long? UsedBytes(string root)
    {
        try
        {
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.TotalSize - drive.TotalFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void OnProgress(long generation, ScanProgress value)
    {
        if (generation != _generation || !IsScanning)
        {
            return;
        }

        var files = LocalFileSystem.FormatGrouped((ulong)value.Files);
        if (_estimate.Percent(value) is not { } percent)
        {
            Status = string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.scanning"), files);
            return;
        }

        ProgressPercent = percent;
        var shown = percent.ToString("0", CultureInfo.InvariantCulture);
        Status = _estimate.Files > 0
            ? string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.scanning_of"), files, LocalFileSystem.FormatGrouped((ulong)_estimate.Files), shown)
            : string.Format(
                CultureInfo.InvariantCulture,
                _text.Get("diskusage.scanning_bytes"),
                files,
                LocalFileSystem.FormatSize((ulong)value.Bytes),
                LocalFileSystem.FormatSize((ulong)_estimate.Bytes),
                shown);
    }

    /// <summary>The name a ring segment shows inside it when it fits; empty for a merged "other" segment.</summary>
    public string LabelOf(RingSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Node is { Kind: not SizeNodeKind.SmallFiles } node ? DiskUsageRow.NameOf(node, _text) : "";
    }

    private void Show(SizeNode node)
    {
        Current = node;
        PathText = node.Path;
        CanGoUp = !ReferenceEquals(node, _root) && node.Parent is not null;
        var segments = RingLayout.Build(node);
        var firsts = segments.Where(segment => segment.Level == 1).ToList();
        Rows.Clear();
        foreach (var child in node.Children)
        {
            // The bar takes the colour of the child's ring segment, so the list and the ring read together.
            var branch = firsts.FindIndex(segment => ReferenceEquals(segment.Node, child));
            var color = branch < 0 ? DiskUsageHeat.Other : DiskUsageHeat.ForBranch(branch, 1);
            Rows.Add(new DiskUsageRow(child, node, _text, new SolidColorBrush(color)));
        }

        Selected = Rows.FirstOrDefault();
        Segments = segments;
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
