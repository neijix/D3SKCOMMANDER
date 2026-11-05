using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Search;

namespace D3skCommander.App.Features.Search;

/// <summary>
/// The search window: the form fields, Start and Cancel, and the results in the shared file list.
/// The query is parsed by <see cref="SearchQueryBuilder"/>; the walk runs as a job and its events
/// wait in a queue until <see cref="Drain"/> shows them (the view calls it on a timer).
/// </summary>
public sealed partial class SearchViewModel : ObservableObject, IShellWidget
{
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly IFolderPicker _picker;
    private readonly IPanelWorkspace _panels;
    private readonly SearchEngine _engine;
    private readonly FileList _results;

    [ObservableProperty]
    private string _roots = "";

    [ObservableProperty]
    private string _nameMask = "*";

    [ObservableProperty]
    private bool _nameIsRegex;

    [ObservableProperty]
    private string _contentText = "";

    [ObservableProperty]
    private bool _textIsRegex;

    [ObservableProperty]
    private bool _caseSensitive;

    [ObservableProperty]
    private bool _wholeWords;

    [ObservableProperty]
    private bool _notContaining;

    [ObservableProperty]
    private bool _depthLimited;

    [ObservableProperty]
    private decimal? _depth = 1;

    [ObservableProperty]
    private bool _includeHidden;

    [ObservableProperty]
    private bool _includeFiles = true;

    [ObservableProperty]
    private bool _includeFolders = true;

    [ObservableProperty]
    private bool _inArchives;

    [ObservableProperty]
    private string _minSize = "";

    [ObservableProperty]
    private string _maxSize = "";

    [ObservableProperty]
    private string _newerDays = "";

    [ObservableProperty]
    private string _olderDays = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _errorsLine = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _hitCount;

    public SearchViewModel(
        ILocalizer text,
        IJobService jobs,
        IFolderPicker picker,
        IPanelWorkspace panels,
        SearchEngine engine)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(engine);
        _text = text;
        _jobs = jobs;
        _picker = picker;
        _panels = panels;
        _engine = engine;
        _results = new FileList(text);
        _status = text.Get("search.status.ready");
        _results.ShowRows(SearchRows.Columns, []);
        StartCommand = new RelayCommand(Start);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
        GoToFileCommand = new AsyncRelayCommand(() => GoToCursorAsync(), () => HitCount > 0);
    }

    public string? Name => "Search";

    /// <summary>The results table: the shared file list control's model.</summary>
    public FileList Results => _results;

    public IRelayCommand StartCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand BrowseCommand { get; }

    public IAsyncRelayCommand GoToFileCommand { get; }

    public SearchView CreateView() => new(this, _text);

    public Avalonia.Controls.Control CreateContent() => CreateView();

    /// <summary>The folder the next search starts in, unless a search is running.</summary>
    public void Prepare(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!IsRunning && folder.Length > 0)
        {
            Roots = folder;
        }
    }

    partial void OnIsRunningChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    partial void OnHitCountChanged(int value) => GoToFileCommand.NotifyCanExecuteChanged();

    private async Task BrowseAsync()
    {
        var first = SearchQueryBuilder.SplitRoots(Roots);
        var picked = await _picker.PickAsync(first.Count > 0 ? first[0] : null).ConfigureAwait(true);
        if (picked is not null)
        {
            Roots = picked;
        }
    }

    private SearchForm ReadForm() => new()
    {
        Roots = Roots,
        Name = NameMask,
        NameIsRegex = NameIsRegex,
        Text = ContentText,
        TextIsRegex = TextIsRegex,
        CaseSensitive = CaseSensitive,
        WholeWords = WholeWords,
        NotContaining = NotContaining,
        DepthLimited = DepthLimited,
        Depth = (int)Math.Clamp(Depth ?? 1, 1, 10_000),
        IncludeHidden = IncludeHidden,
        Files = IncludeFiles,
        Dirs = IncludeFolders,
        InArchives = InArchives,
        MinSize = MinSize,
        MaxSize = MaxSize,
        NewerDays = NewerDays,
        OlderDays = OlderDays,
    };
}
