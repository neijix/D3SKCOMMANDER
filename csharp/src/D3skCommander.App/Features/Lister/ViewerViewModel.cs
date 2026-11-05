using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.Lister;

/// <summary>
/// The viewer tool window's state over Core's <see cref="FileViewer"/>. A file opens off the UI
/// thread: the name and a busy state show at once, then the file or its typed error. Text and
/// hex show one <see cref="ViewerLimits.PageSize"/> page at a time (<see cref="ViewerPages"/>):
/// a scroll bar over the whole file jumps, the page edges load the neighbours, and find scans
/// the whole file as a job. Rows are built on demand, so a page of a million rows costs nothing
/// until it scrolls.
/// </summary>
public sealed class ViewerViewModel : ObservableObject, IShellWidget
{
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly Func<string, FileViewer> _open;
    private FileViewer? _viewer;
    private ViewerPage? _page;
    private PageRows? _shown;
    private IJob? _findJob;
    private int _ticket;
    private int _columns = 100;
    private string _name = "";
    private bool _isBusy;
    private string _error = "";
    private string _notice = "";
    private string _status = "";
    private string _header = "";
    private IReadOnlyList<ViewerLine> _rows = [];
    private IReadOnlyList<ViewerModeChoice> _modes = [];
    private ViewerModeChoice? _selectedMode;
    private int _selectedRow = -1;
    private int? _cursorLine;
    private long? _hexCursor;
    private long? _matchOffset;
    private double _scrollValue;
    private bool _wrap = true;
    private bool _matchCase;
    private string _findText = "";
    private string _filter = "";
    private Bitmap? _image;

    public ViewerViewModel(ILocalizer text, IJobService jobs)
        : this(text, jobs, FileViewer.Open)
    {
    }

    public ViewerViewModel(ILocalizer text, IJobService jobs, Func<string, FileViewer> open)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(open);
        _text = text;
        _jobs = jobs;
        _open = open;
    }

    /// <summary>Raised with a row of <see cref="Rows"/> the view should bring into view (a page edge crossed).</summary>
    public event Action<int>? RevealRequested;

    public string? Name => "Lister";

    public string FileName
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The typed error as localized text plus the path; empty when the file opened.</summary>
    public string ErrorMessage
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _error.Length > 0;

    public string Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    /// <summary>Encoding and size, or the table's note.</summary>
    public string StatusText
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Column titles above the rows in table mode.</summary>
    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    public IReadOnlyList<ViewerLine> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public IReadOnlyList<ViewerModeChoice> Modes
    {
        get => _modes;
        private set => SetProperty(ref _modes, value);
    }

    public ViewerModeChoice? SelectedMode
    {
        get => _selectedMode;
        private set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(IsText));
                OnPropertyChanged(nameof(IsHex));
                OnPropertyChanged(nameof(IsTable));
                OnPropertyChanged(nameof(IsImage));
                OnPropertyChanged(nameof(CanFind));
                OnPropertyChanged(nameof(HasPageScroll));
            }
        }
    }

    public bool IsText => _selectedMode?.Mode == ViewerMode.Text;

    public bool IsHex => _selectedMode?.Mode == ViewerMode.Hex;

    public bool IsTable => _selectedMode?.Mode == ViewerMode.Table;

    public bool IsImage => _selectedMode?.Mode == ViewerMode.Image;

    /// <summary>The decoded image of Image mode; null when the file is not an image or did not decode.</summary>
    public Bitmap? Image => _image;

    public bool CanFind => IsText || IsHex;

    /// <summary>True when the page scroll bar over the whole file is shown (text and hex).</summary>
    public bool HasPageScroll => CanFind;

    /// <summary>The scroll bar's range: the file size in bytes in text mode, the row count (offset / 16) in hex mode.</summary>
    public double ScrollMaximum
    {
        get
        {
            if (_viewer is null)
            {
                return 0;
            }

            if (IsHex)
            {
                return HexRowCount > 1 ? HexRowCount - 1 : 0;
            }

            return _viewer.Size;
        }
    }

    /// <summary>The first loaded position on the scroll range, moved as the list scrolls.</summary>
    public double ScrollValue
    {
        get => _scrollValue;
        private set => SetProperty(ref _scrollValue, value);
    }

    /// <summary>The part of the scroll range the loaded page covers.</summary>
    public double ScrollViewport => _page is null ? 0 : IsHex ? _page.HexRows.Count : _page.End - _page.Start;

    /// <summary>The display row to highlight and keep in view; -1 for none.</summary>
    public int SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (SetProperty(ref _wrap, value) && _viewer is { } viewer)
            {
                Reshow(viewer);
            }
        }
    }

    public bool MatchCase
    {
        get => _matchCase;
        set => SetProperty(ref _matchCase, value);
    }

    public string FindText
    {
        get => _findText;
        set => SetProperty(ref _findText, value ?? "");
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? "") && _viewer is { Mode: ViewerMode.Table } viewer)
            {
                viewer.SetFilter(_filter);
                Reshow(viewer);
            }
        }
    }

    /// <summary>The index, among the loaded page's lines, of the last find hit in text mode.</summary>
    public int? TextCursor => _cursorLine;

    /// <summary>The file offset of the last find hit in hex mode.</summary>
    public long? HexCursor => _hexCursor;

    public string WrapLabel => _text.Get("lister.wrap");

    public string MatchCaseLabel => _text.Get("lister.match_case");

    public string FindLabel => _text.Get("lister.find");

    public string NextLabel => _text.Get("lister.find_next");

    public string PreviousLabel => _text.Get("lister.find_prev");

    public string FilterLabel => _text.Get("lister.filter");

    /// <summary>The opened file's path, or empty.</summary>
    public string Path => _viewer?.Path ?? "";

    private long HexRowCount => (((long)(_viewer?.Size ?? 0)) + ViewerLimits.HexWidth - 1) / ViewerLimits.HexWidth;

    public void ReleaseImage()
    {
        _image?.Dispose();
        _image = null;
        OnPropertyChanged(nameof(Image));
    }

    public Control CreateContent() => new ViewerView { DataContext = this };

    /// <summary>
    /// Sets how many characters fit across a row, gutter included (the view measures it with the
    /// row font). A change rebuilds the wrapped text rows.
    /// </summary>
    public void SetColumns(int columns)
    {
        var next = Math.Max(ViewerText.MinWrapColumns, columns);
        if (next == _columns)
        {
            return;
        }

        _columns = next;
        if (_viewer is { Mode: ViewerMode.Text } viewer && _wrap)
        {
            Reshow(viewer);
        }
    }

    /// <summary>Shows <paramref name="path"/>: the name and busy state at once, the file when the load ends.</summary>
    public async Task OpenAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _findJob?.Cancel();
        var ticket = ++_ticket;
        Begin(path);
        var loaded = await Task.Run(() => Load(path)).ConfigureAwait(true);
        if (ticket != _ticket)
        {
            loaded.Image?.Dispose();
            return;
        }

        UiThread.Invoke(() => Show(loaded, true));
    }

    public async Task SetModeAsync(ViewerMode mode)
    {
        if (_viewer is not { } viewer || viewer.Mode == mode || !viewer.Modes.Contains(mode))
        {
            return;
        }

        var ticket = ++_ticket;
        var at = _page?.Start ?? 0;
        IsBusy = true;
        var loaded = await Task.Run(() =>
        {
            if (mode is ViewerMode.Text or ViewerMode.Hex)
            {
                viewer.UsePagedMode(mode);
            }
            else
            {
                viewer.SetMode(mode);
            }

            return Prepare(viewer, at, false);
        }).ConfigureAwait(true);
        if (ticket == _ticket)
        {
            UiThread.Invoke(() => Show(loaded, true));
        }
    }

    /// <summary>
    /// Finds the next or previous hit of <see cref="FindText"/> in the whole file, as a cancelable
    /// job, and shows it: its page loads and its row is selected. Not found sets the notice.
    /// </summary>
    public async Task<bool> FindAsync(bool forward)
    {
        if (_viewer is not { } viewer || _page is null || !CanFind || _findText.Length == 0)
        {
            return false;
        }

        _findJob?.Cancel();
        Notice = "";
        var request = new FindRequest(viewer, _findText, _matchCase, FindFrom(viewer, forward), forward);
        var found = default(ViewerFound);
        var job = _jobs.Start(
            _text.Get("lister.find_job").Replace("{name}", FileName, StringComparison.Ordinal),
            (progress, cancellation) =>
            {
                found = Scan(request, progress, cancellation);
                return Task.CompletedTask;
            });
        _findJob = job;
        var result = await job.Completion.ConfigureAwait(true);
        if (!ReferenceEquals(_findJob, job) || result.Outcome == JobOutcome.Cancelled)
        {
            return false;
        }

        _findJob = null;
        var error = found.Error ?? result.Failed.FirstOrDefault()?.Error;
        if (error is not null)
        {
            Notice = ErrorLine(error, viewer.Path);
            return false;
        }

        if (found.Match is not { } match)
        {
            Notice = _text.Get("lister.not_found");
            return false;
        }

        await ShowMatchAsync(match).ConfigureAwait(true);
        return true;
    }

    /// <summary>Scrolls to <paramref name="position"/> of the scroll range (see <see cref="ScrollMaximum"/>); the end shows the last rows.</summary>
    public Task JumpAsync(double position)
    {
        if (_viewer is null || _page is null)
        {
            return Task.CompletedTask;
        }

        var toEnd = position >= ScrollMaximum;
        var offset = toEnd ? long.MaxValue : (long)position * (IsHex ? ViewerLimits.HexWidth : 1);
        return GoToAsync(new PageGoal(offset, false, null, false, toEnd));
    }

    public Task GoToEndAsync() => JumpAsync(ScrollMaximum);

    /// <summary>Loads the page after the current one, shown from its first row. Does nothing at the end.</summary>
    public Task NextPageAsync() =>
        _page is { AtEnd: false } page ? GoToAsync(new PageGoal(page.End, true, null, false, false)) : Task.CompletedTask;

    /// <summary>Loads the page before the current one, keeping the first row of the current page in view.</summary>
    public Task PreviousPageAsync()
    {
        if (_page is not { Start: > 0 } page)
        {
            return Task.CompletedTask;
        }

        return GoToAsync(new PageGoal(Math.Max(0, page.Start - ViewerLimits.PageSize), false, page.Start, false, false));
    }

    /// <summary>Moves the scroll bar to <paramref name="fraction"/> (0 to 1) of the loaded page.</summary>
    public void SetScrollFraction(double fraction)
    {
        if (_page is null)
        {
            return;
        }

        var origin = IsHex ? _page.Start / ViewerLimits.HexWidth : _page.Start;
        ScrollValue = Math.Min(ScrollMaximum, origin + (Math.Clamp(fraction, 0, 1) * ScrollViewport));
    }

    private void Begin(string path)
    {
        FileName = System.IO.Path.GetFileName(path);
        IsBusy = true;
        ErrorMessage = "";
        Notice = _text.Get("lister.opening").Replace("{name}", FileName, StringComparison.Ordinal);
        Rows = [];
        Header = "";
        SelectedRow = -1;
        _page = null;
        _shown = null;
        ReleaseImage();
        _cursorLine = null;
        _hexCursor = null;
        _matchOffset = null;
    }

    private Loaded Load(string path)
    {
        var viewer = _open(path);
        if (viewer.Error is not null)
        {
            return new Loaded(viewer, null, null, [], viewer.Error);
        }

        if (viewer.Mode == ViewerMode.Image)
        {
            return LoadImage(viewer);
        }

        return Prepare(viewer, 0, false);
    }

    /// <summary>Decodes an image file; one that does not decode opens as bytes, with a notice.</summary>
    private Loaded LoadImage(FileViewer viewer)
    {
        var image = ViewerImage.Open(viewer);
        return image is null
            ? Prepare(viewer, 0, false) with { Notice = _text.Get("lister.image_failed") }
            : new Loaded(viewer, null, null, [], null) { Image = image };
    }

    /// <summary>Reads the page at <paramref name="offset"/> (text, hex) or builds the table rows.</summary>
    private Loaded Prepare(FileViewer viewer, long offset, bool exact)
    {
        if (viewer.Mode == ViewerMode.Image)
        {
            return new Loaded(viewer, null, null, [], null) { Image = _image };
        }

        if (viewer.Mode is not (ViewerMode.Text or ViewerMode.Hex))
        {
            return new Loaded(viewer, null, null, ViewerPageRows.Table(viewer), null);
        }

        var read = ViewerPages.Read(viewer.Path, offset, viewer.Mode, viewer.Encoding, exact);
        if (read.Page is not { } page)
        {
            return new Loaded(viewer, null, null, [], read.Error);
        }

        var shown = ViewerPageRows.Build(page, viewer.Mode, _wrap, _columns);
        return new Loaded(viewer, page, shown, shown.Lines, null);
    }

    private void Show(Loaded loaded, bool reset)
    {
        var viewer = loaded.Viewer;
        _viewer = viewer;
        IsBusy = false;
        Notice = loaded.Notice ?? "";
        SelectedRow = -1;
        _image = loaded.Image ?? _image;
        OnPropertyChanged(nameof(Image));

        if (loaded.Error is { } error)
        {
            ErrorMessage = ErrorLine(error, viewer.Path);
            Modes = [];
            SelectedMode = null;
            Rows = [];
            StatusText = "";
            _page = null;
            return;
        }

        ErrorMessage = "";
        _page = loaded.Page;
        _shown = loaded.Shown;
        if (reset)
        {
            _cursorLine = null;
            _hexCursor = null;
        }

        var choices = new List<ViewerModeChoice>();
        foreach (var mode in viewer.Modes.Where(mode => mode != ViewerMode.Image || _image is not null))
        {
            choices.Add(new ViewerModeChoice(mode, _text.Get("lister.mode." + mode.ToString().ToLowerInvariant())));
        }

        Modes = choices;
        SelectedMode = choices.Find(choice => choice.Mode == viewer.Mode);
        _filter = viewer.Filter;
        OnPropertyChanged(nameof(Filter));
        Display(viewer, loaded.Rows);
        OnPropertyChanged(nameof(Path));
        PageChanged();
    }

    private void PageChanged()
    {
        OnPropertyChanged(nameof(TextCursor));
        OnPropertyChanged(nameof(HexCursor));
        OnPropertyChanged(nameof(ScrollMaximum));
        OnPropertyChanged(nameof(ScrollViewport));
        SetScrollFraction(0);
    }

    private void Display(FileViewer viewer, IReadOnlyList<ViewerLine> rows)
    {
        Rows = rows;
        Header = viewer.Table is { } table && viewer.Mode == ViewerMode.Table ? ViewerRowText.Header(table) : "";
        if (viewer.Mode == ViewerMode.Table)
        {
            StatusText = viewer.Table?.Note ?? "";
            return;
        }

        if (viewer.Mode == ViewerMode.Image && _image is not null)
        {
            StatusText = ViewerImage.Status(_image, viewer.Size);
            return;
        }

        StatusText = viewer.EncodingName + " | " + LocalFileSystem.FormatSize(viewer.Size);
    }

    private void Reshow(FileViewer viewer)
    {
        if (_page is { } page && viewer.Mode is ViewerMode.Text or ViewerMode.Hex)
        {
            _shown = ViewerPageRows.Build(page, viewer.Mode, _wrap, _columns);
            Rows = _shown.Lines;
            SelectedRow = CursorRow();
            return;
        }

        SelectedRow = -1;
        Display(viewer, ViewerPageRows.Table(viewer));
    }

    /// <summary>Loads another page off the UI thread; a result for a goal no longer wanted is dropped.</summary>
    private async Task GoToAsync(PageGoal goal)
    {
        if (_viewer is not { } viewer)
        {
            return;
        }

        var ticket = ++_ticket;
        IsBusy = true;
        var loaded = await Task.Run(() => Prepare(viewer, goal.Offset, goal.Exact)).ConfigureAwait(true);
        if (ticket != _ticket)
        {
            return;
        }

        IsBusy = false;
        if (loaded.Page is null)
        {
            Notice = ErrorLine(loaded.Error ?? OperationError.Other("unreadable", viewer.Path), viewer.Path);
            return;
        }

        Notice = "";
        _page = loaded.Page;
        _shown = loaded.Shown;
        _cursorLine = null;
        _hexCursor = null;
        Rows = loaded.Rows;
        SelectedRow = -1;
        if (goal.Focus is long focus)
        {
            Focus(focus, goal.Select);
        }
        else if (goal.ToEnd)
        {
            RevealRequested?.Invoke(Rows.Count - 1);
            SelectedRow = Rows.Count - 1;
        }

        PageChanged();
    }

    private async Task ShowMatchAsync(ViewerMatch match)
    {
        _matchOffset = match.Offset;
        if (_page is { } page && match.Offset >= page.Start && match.Offset + match.Length <= page.End)
        {
            Focus(match.Offset, true);
            return;
        }

        var around = Math.Max(0, match.Offset - (ViewerLimits.PageSize / 2));
        await GoToAsync(new PageGoal(around, false, match.Offset, true, false)).ConfigureAwait(true);
    }

    /// <summary>Marks the line or hex row holding <paramref name="offset"/> and selects it, or only brings it into view.</summary>
    private void Focus(long offset, bool select)
    {
        if (_page is null)
        {
            return;
        }

        if (_viewer?.Mode == ViewerMode.Hex)
        {
            _hexCursor = select ? offset : null;
        }
        else
        {
            _cursorLine = select ? _page.LineAt(offset) : null;
        }

        var row = select ? CursorRow() : RowAt(offset);
        if (select)
        {
            SelectedRow = row;
        }
        else
        {
            RevealRequested?.Invoke(row);
        }

        OnPropertyChanged(nameof(TextCursor));
        OnPropertyChanged(nameof(HexCursor));
    }

    private int CursorRow()
    {
        if (_hexCursor is long hex && _page is not null)
        {
            return _page.HexRowAt(hex);
        }

        return _cursorLine is int line && _shown is not null ? _shown.RowOfLine(line) : -1;
    }

    private int RowAt(long offset)
    {
        if (_page is null)
        {
            return -1;
        }

        return _viewer?.Mode == ViewerMode.Hex ? _page.HexRowAt(offset) : _shown?.RowOfLine(_page.LineAt(offset)) ?? -1;
    }

    private long FindFrom(FileViewer viewer, bool forward)
    {
        if (_matchOffset is long match)
        {
            return forward ? match + 1 : match;
        }

        return forward ? _page?.Start ?? 0 : (long)viewer.Size;
    }

    private ViewerFound Scan(FindRequest request, IJobProgress progress, CancellationToken cancellation)
    {
        var viewer = request.Viewer;
        var total = (long)viewer.Size;
        var done = 0L;
        return ViewerSearch.Find(
            viewer.Path,
            request.Query,
            request.MatchCase,
            viewer.Mode,
            viewer.Encoding,
            request.From,
            request.Forward,
            step =>
            {
                done += step;
                progress.Report(new JobProgress(0, 1, Math.Min(done, total), total, FileName));
            },
            cancellation);
    }

    private string ErrorLine(OperationError error, string path) => ErrorLines.Describe(_text, error, path);

    private sealed record Loaded(
        FileViewer Viewer,
        ViewerPage? Page,
        PageRows? Shown,
        IReadOnlyList<ViewerLine> Rows,
        OperationError? Error)
    {
        public Bitmap? Image { get; init; }

        public string? Notice { get; init; }
    }

    private sealed record FindRequest(FileViewer Viewer, string Query, bool MatchCase, long From, bool Forward);

    /// <summary>Where a page load goes: the offset to read, and what to do with the result.</summary>
    private sealed record PageGoal(long Offset, bool Exact, long? Focus, bool Select, bool ToEnd);
}
