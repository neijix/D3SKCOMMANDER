using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Shell;
using D3skCommander.Core.Compare;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Compare;

/// <summary>
/// Side-by-side compare: text rows, a binary peek, or the two editors. <see cref="Refresh"/> polls the
/// session and re-reads what the view shows; the view calls it on a timer while attached.
/// </summary>
public sealed partial class CompareViewModel : ObservableObject, IShellWidget
{
    private readonly IJobService _jobs;
    private readonly ILocalizer _text;
    private readonly Action? _saved;
    private CompareSession _session;
    private CompareOutcome? _peekOf;
    private CompareOutcome? _rowsOf;
    private (int Start, int End) _marked;
    private bool _started;
    private bool _forceView;

    [ObservableProperty]
    private string _leftPath;

    [ObservableProperty]
    private string _rightPath;

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    private bool _ignoreWhitespace;

    [ObservableProperty]
    private CompareSurface _surface;

    [ObservableProperty]
    private string _surfaceText = "";

    [ObservableProperty]
    private string _statusLine = "";

    [ObservableProperty]
    private string _blockLabel = "";

    [ObservableProperty]
    private bool _canNavigate;

    [ObservableProperty]
    private bool _canChooseMode;

    [ObservableProperty]
    private bool _canSave;

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private bool _isIdentical;

    [ObservableProperty]
    private bool _hasPeek;

    public CompareViewModel(string left, string right, IJobService jobs, ILocalizer text, Action? saved)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(text);
        _jobs = jobs;
        _text = text;
        _saved = saved;
        _leftPath = left;
        _rightPath = right;
        _session = OpenSession(left, right);
    }

    /// <summary>Raised with a row index the view should bring into view.</summary>
    public event Action<int>? ScrollRequested;

    public string? Name => "CompareFiles";

    public CompareSession Session => _session;

    public ObservableCollection<CompareRow> Rows { get; } = [];

    public ObservableCollection<ComparePeekLine> PeekLeft { get; } = [];

    public ObservableCollection<ComparePeekLine> PeekRight { get; } = [];

    /// <summary>The rows as "left|right" lines, for tests and the status of what is shown.</summary>
    public string DrawnRows => string.Concat(Rows.Select(row => row.Left + "|" + row.Right + "\n"));

    public bool ShowingEdit => _session.Mode == CompareMode.Edit && !_forceView;

    public string LeftHeader => _text.Get("compare.left");

    public string RightHeader => _text.Get("compare.right");

    public string SwapText => _text.Get("compare.swap");

    public string RunText => _text.Get("compare.compare");

    public string PreviousText => _text.Get("compare.previous");

    public string NextText => _text.Get("compare.next");

    public string IgnoreCaseText => _text.Get("compare.ignore_case");

    public string IgnoreWhitespaceText => _text.Get("compare.ignore_whitespace");

    public string ViewText => _text.Get("compare.view");

    public string EditText => _text.Get("cmd.edit");

    public string RecompareText => _text.Get("compare.recompare");

    public string SaveLeftText => _text.Get("compare.save_left");

    public string SaveRightText => _text.Get("compare.save_right");

    public string WorkingText => _text.Get("compare.working");

    public string IdenticalText => _text.Get("compare.identical");

    public bool IsText => Surface == CompareSurface.Text;

    public bool IsBinary => Surface == CompareSurface.Binary;

    public bool IsEdit => Surface == CompareSurface.Edit;

    public bool HasMessage => MessageText.Length > 0;

    /// <summary>The line shown above the body for pending, error and binary results.</summary>
    public string MessageText => Surface is CompareSurface.Text or CompareSurface.Edit ? "" : SurfaceText;

    public void Draw()
    {
    }

    public Control CreateContent() => new CompareView(this);

    public void Restart(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        LeftPath = left;
        RightPath = right;
        _forceView = false;
        ResetEditSources();
        _started = true;
        _session = OpenSession(left, right);
        OnPropertyChanged(nameof(Session));
        _session.CompareFiles();
        Refresh();
    }

    [RelayCommand]
    public void NextBlock()
    {
        _session.GoToDifference(true);
        Refresh();
    }

    [RelayCommand]
    public void PreviousBlock()
    {
        _session.GoToDifference(false);
        Refresh();
    }

    [RelayCommand]
    public void BeginEdit()
    {
        _session.EnterEdit();
        _forceView = _session.Mode != CompareMode.Edit;
        Refresh();
    }

    [RelayCommand]
    public void ShowCompareView()
    {
        _forceView = true;
        Refresh();
    }

    [RelayCommand]
    public void Swap()
    {
        (LeftPath, RightPath) = (RightPath, LeftPath);
        RunCompare();
    }

    [RelayCommand]
    public void RunCompare()
    {
        _forceView = false;
        ResetEditSources();
        _session.LeftPath = LeftPath.Trim();
        _session.RightPath = RightPath.Trim();
        _session.CompareFiles();
        Refresh();
    }

    [RelayCommand]
    public void RecompareBuffers()
    {
        _session.RecompareBuffers();
        Refresh();
    }

    [RelayCommand]
    public void SaveLeft() => SaveSide(0);

    [RelayCommand]
    public void SaveRight() => SaveSide(1);

    /// <summary>Polls the session and updates every shown value from it.</summary>
    public void Refresh()
    {
        if (!_started)
        {
            _started = true;
            _session.CompareFiles();
        }

        _session.Poll();
        var outcome = _session.Outcome;
        var kind = outcome.Kind;
        CanNavigate = kind == CompareKind.Text;
        CanChooseMode = kind == CompareKind.Text || _session.LeftBuffer is not null;
        CanSave = CanChooseMode && ShowingEdit;
        IsPending = _session.HasPendingResult;
        StatusLine = CompareText.Status(_text, _session.Status);
        BlockLabel = Fill(_text.Get("compare.blocks"), outcome.Blocks);
        ShowBody(outcome);
        UpdateRows(outcome);
        OnPropertyChanged(nameof(ShowingEdit));
        OnPropertyChanged(nameof(MessageText));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(DrawnRows));
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsBinary));
        OnPropertyChanged(nameof(IsEdit));
    }

    partial void OnIgnoreCaseChanged(bool value) => Reoptions();

    partial void OnIgnoreWhitespaceChanged(bool value) => Reoptions();

    private void Reoptions()
    {
        var options = new DiffOptions(IgnoreCase, IgnoreWhitespace);
        if (options == _session.Options)
        {
            return;
        }

        _session.Options = options;
        if (ShowingEdit)
        {
            _session.Rerun();
            Refresh();
            return;
        }

        RunCompare();
    }

    private void SaveSide(int side)
    {
        _session.Save(side);
        Refresh();
    }

    private CompareSession OpenSession(string left, string right)
    {
        var title = _text.Get("tool.compare.title");
        var session = new CompareSession(left, right, new CompareJobTitles(_jobs, title));
        session.ReloadRequested += () => _saved?.Invoke();
        return session;
    }

    private void ShowBody(CompareOutcome outcome)
    {
        IsIdentical = false;
        HasPeek = false;
        if (ShowingEdit)
        {
            Surface = CompareSurface.Edit;
            SurfaceText = _session.LeftBuffer?.Path ?? "";
            UpdateEditors();
            return;
        }

        if (outcome.Kind == CompareKind.Error)
        {
            Surface = CompareSurface.Error;
            var message = string.IsNullOrEmpty(outcome.Error) ? _text.Get("error.other") : outcome.Error;
            SurfaceText = CompareText.Status(_text, CompareStatus.Error(message));
        }
        else if (outcome.Kind == CompareKind.Binary)
        {
            Surface = CompareSurface.Binary;
            ShowBinary(outcome);
        }
        else if (outcome.Kind == CompareKind.Text)
        {
            Surface = CompareSurface.Text;
            SurfaceText = BlockLabel;
            IsIdentical = outcome.Rows.Count > 0 && outcome.Rows.All(row => row.Kind == RowKind.Equal);
        }
        else
        {
            Surface = _session.HasPendingResult ? CompareSurface.Pending : CompareSurface.Empty;
            SurfaceText = Surface == CompareSurface.Pending ? _text.Get("compare.working") : "";
        }
    }

    private void ShowBinary(CompareOutcome outcome)
    {
        SurfaceText = "";
        if (outcome.Identical)
        {
            SurfaceText = _text.Get("compare.identical");
        }
        else if (outcome.FirstDifference is long offset)
        {
            SurfaceText = CompareText.Offset(_text, offset);
        }

        HasPeek = !outcome.Identical && outcome.Peek is not null;
        if (ReferenceEquals(_peekOf, outcome))
        {
            return;
        }

        PeekLeft.Clear();
        PeekRight.Clear();
        if (outcome.Peek is { } peek && !outcome.Identical)
        {
            Fill(PeekLeft, ComparePeekLine.Side(peek, peek.Left, peek.Right));
            Fill(PeekRight, ComparePeekLine.Side(peek, peek.Right, peek.Left));
        }

        _peekOf = outcome;
    }

    private void UpdateRows(CompareOutcome outcome)
    {
        if (outcome.Kind != CompareKind.Binary && _peekOf is not null)
        {
            PeekLeft.Clear();
            PeekRight.Clear();
            _peekOf = null;
        }

        if (!ReferenceEquals(_rowsOf, outcome))
        {
            Rows.Clear();
            if (outcome.Kind == CompareKind.Text)
            {
                Fill(Rows, outcome.Rows.Select(row => new CompareRow(row)));
            }

            _rowsOf = outcome;
            _marked = default;
        }

        MarkBlock(outcome);
        if (_session.ScrollTo is int target)
        {
            _session.AcknowledgeScroll();
            ScrollRequested?.Invoke(target);
        }
    }

    private void MarkBlock(CompareOutcome outcome)
    {
        var span = (Start: 0, End: 0);
        if (_session.Current is int current && outcome.Kind == CompareKind.Text)
        {
            var block = DiffNavigator.BlockRange(outcome.Rows, current);
            span = (block.Start, block.End);
        }

        if (span == _marked)
        {
            return;
        }

        SetBlock(_marked, false);
        SetBlock(span, true);
        _marked = span;
    }

    private void SetBlock((int Start, int End) span, bool value)
    {
        for (var i = span.Start; i < span.End && i < Rows.Count; i++)
        {
            Rows[i].InBlock = value;
        }
    }

    private string Fill(string template, int count) =>
        CompareText.Fill(template, "{n}", count.ToString(CultureInfo.InvariantCulture));

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
