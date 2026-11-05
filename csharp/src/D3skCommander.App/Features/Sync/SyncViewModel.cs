using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Sync;

namespace D3skCommander.App.Features.Sync;

/// <summary>
/// The sync window: two roots, mode and criteria, then the reviewable plan. Compare and Run are jobs;
/// <see cref="Refresh"/> picks up their progress and end, and the view calls it on a timer while attached.
/// </summary>
public sealed partial class SyncViewModel : ObservableObject, IShellWidget
{
    private readonly IJobService _jobs;
    private readonly ILocalizer _text;
    private readonly IFolderPicker _folders;
    private readonly Action? _applied;
    private readonly List<SyncRow> _all = [];
    private readonly List<SyncItem> _items = [];

    [ObservableProperty]
    private string _leftPath;

    [ObservableProperty]
    private string _rightPath;

    [ObservableProperty]
    private SyncModeOption _modeOption;

    [ObservableProperty]
    private SyncCriteriaOption _criteriaOption;

    [ObservableProperty]
    private bool _emptyDirs = true;

    [ObservableProperty]
    private bool _showEqual;

    [ObservableProperty]
    private bool _showNewer = true;

    [ObservableProperty]
    private bool _showLeftOnly = true;

    [ObservableProperty]
    private bool _showRightOnly = true;

    [ObservableProperty]
    private string _statusLine = "";

    [ObservableProperty]
    private string _countsLine = "";

    [ObservableProperty]
    private string _pendingLine = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canRun;

    [ObservableProperty]
    private string _equalText = "";

    [ObservableProperty]
    private string _newerText = "";

    [ObservableProperty]
    private string _leftOnlyText = "";

    [ObservableProperty]
    private string _rightOnlyText = "";

    public SyncViewModel(
        string left,
        string right,
        IJobService jobs,
        ILocalizer text,
        IFolderPicker folders,
        Action? applied)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(folders);
        _jobs = jobs;
        _text = text;
        _folders = folders;
        _applied = applied;
        _leftPath = left;
        _rightPath = right;
        Modes = ModeOptions();
        CriteriaChoices = CriteriaOptions();
        _modeOption = Modes[0];
        _criteriaOption = CriteriaChoices[0];
        UpdateCounts();
    }

    public string? Name => "Sync";

    /// <summary>The rows the class filter lets through.</summary>
    public ObservableCollection<SyncRow> Rows { get; } = [];

    /// <summary>The failed items of the last run, one localized line each.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    public IReadOnlyList<SyncModeOption> Modes { get; }

    public IReadOnlyList<SyncCriteriaOption> CriteriaChoices { get; }

    public SyncMode Mode
    {
        get => ModeOption.Mode;
        set => ModeOption = Modes.First(option => option.Mode == value);
    }

    public SyncCriteria Criteria
    {
        get => CriteriaOption.Criteria;
        set => CriteriaOption = CriteriaChoices.First(option => option.Criteria == value);
    }

    public IReadOnlyList<SyncItem> Items => _items;

    public bool HasFailures => Failures.Count > 0;

    public string LeftLabel => _text.Get("sync.left");

    public string RightLabel => _text.Get("sync.right");

    public string BrowseText => _text.Get("sync.browse");

    public string ModeLabel => _text.Get("sync.mode");

    public string CriteriaLabel => _text.Get("sync.criteria");

    public string EmptyDirsText => _text.Get("sync.empty_dirs");

    public string CompareText => _text.Get("sync.compare");

    public string CancelText => _text.Get("sync.cancel");

    public string RunText => _text.Get("sync.run");

    public string NameHeader => _text.Get("sync.col.name");

    public string LeftHeader => _text.Get("sync.col.left");

    public string RightHeader => _text.Get("sync.col.right");

    public string ActionHeader => _text.Get("sync.col.action");

    public Control CreateContent() => new SyncView(this);

    [RelayCommand]
    private async Task BrowseLeft()
    {
        var picked = await _folders.PickAsync(LeftPath).ConfigureAwait(true);
        if (picked is not null)
        {
            LeftPath = picked;
        }
    }

    [RelayCommand]
    private async Task BrowseRight()
    {
        var picked = await _folders.PickAsync(RightPath).ConfigureAwait(true);
        if (picked is not null)
        {
            RightPath = picked;
        }
    }

    partial void OnShowEqualChanged(bool value) => Rebuild();

    partial void OnShowNewerChanged(bool value) => Rebuild();

    partial void OnShowLeftOnlyChanged(bool value) => Rebuild();

    partial void OnShowRightOnlyChanged(bool value) => Rebuild();

    private IReadOnlyList<SyncModeOption> ModeOptions() =>
    [
        new SyncModeOption(SyncMode.Symmetric, _text.Get("sync.mode.symmetric")),
        new SyncModeOption(SyncMode.LeftToRight, _text.Get("sync.mode.left_to_right")),
        new SyncModeOption(SyncMode.RightToLeft, _text.Get("sync.mode.right_to_left")),
    ];

    private IReadOnlyList<SyncCriteriaOption> CriteriaOptions() =>
    [
        new SyncCriteriaOption(SyncCriteria.Date, _text.Get("sync.criteria.date")),
        new SyncCriteriaOption(SyncCriteria.Size, _text.Get("sync.criteria.size")),
        new SyncCriteriaOption(SyncCriteria.Content, _text.Get("sync.criteria.content")),
    ];

    private string Fill(string key, params (string Name, string Value)[] values)
    {
        var result = _text.Get(key);
        foreach (var value in values)
        {
            result = result.Replace("{" + value.Name + "}", value.Value, StringComparison.Ordinal);
        }

        return result;
    }
}

/// <summary>A mode choice with its label.</summary>
public sealed record SyncModeOption(SyncMode Mode, string Label);

/// <summary>A criteria choice with its label.</summary>
public sealed record SyncCriteriaOption(SyncCriteria Criteria, string Label);
