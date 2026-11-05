using System.Globalization;
using System.Text;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.MultiRename;

namespace D3skCommander.App.Features.MultiRename;

/// <summary>
/// Multi-rename tool: masks, search, case and a live preview. The rules live in
/// <see cref="RenameSession"/>; this model edits them and feeds the preview list.
/// </summary>
public sealed partial class RenameViewModel : ObservableObject, IShellWidget
{
    private static readonly (CaseMode Mode, string Key)[] Cases =
    [
        (CaseMode.Unchanged, "rename.case.unchanged"),
        (CaseMode.Lower, "rename.case.lower"),
        (CaseMode.Upper, "rename.case.upper"),
        (CaseMode.FirstUpper, "rename.case.first"),
        (CaseMode.TitleCase, "rename.case.title"),
    ];

    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly FileList _list;
    private RenameSession _session;
    private string? _stamp;
    private int _busy;
    private string _status = "";
    private bool _statusIsError;

    public RenameViewModel(ILocalizer text, IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        _text = text;
        _jobs = jobs;
        _list = new FileList(text);
        _session = new RenameSession([]);
        StartCommand = new RelayCommand(() => BeginBatch(true), () => CanStart);
        UndoCommand = new RelayCommand(() => BeginBatch(false), () => CanUndo);
        PublishPreview();
    }

    /// <summary>Raised on the worker after Start or Undo renames the batch, so the panels can reload.</summary>
    public event Action? BatchChanged;

    public string? Name => "MultiRename";

    public RenameSession Session => _session;

    /// <summary>The preview rows (old name, new name, error).</summary>
    public FileList Preview => _list;

    public IRelayCommand StartCommand { get; }

    public IRelayCommand UndoCommand { get; }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public bool CanStart => _session.CanStart && !IsBusy;

    public bool CanUndo => _session.CanUndo && !IsBusy;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public string NameMask
    {
        get => _session.Rule.NameMask;
        set => Edit(_session.Rule.NameMask, value, v => _session.Rule.NameMask = v, nameof(NameMask));
    }

    public string ExtMask
    {
        get => _session.Rule.ExtMask;
        set => Edit(_session.Rule.ExtMask, value, v => _session.Rule.ExtMask = v, nameof(ExtMask));
    }

    public string Search
    {
        get => _session.Rule.Search;
        set => Edit(_session.Rule.Search, value, v => _session.Rule.Search = v, nameof(Search));
    }

    public string Replace
    {
        get => _session.Rule.Replace;
        set => Edit(_session.Rule.Replace, value, v => _session.Rule.Replace = v, nameof(Replace));
    }

    public bool UseRegex
    {
        get => _session.Rule.UseRegex;
        set => Edit(_session.Rule.UseRegex, value, v => _session.Rule.UseRegex = v, nameof(UseRegex));
    }

    public bool CaseSensitive
    {
        get => _session.Rule.CaseSensitive;
        set => Edit(_session.Rule.CaseSensitive, value, v => _session.Rule.CaseSensitive = v, nameof(CaseSensitive));
    }

    public int CaseIndex
    {
        get => Math.Max(0, Array.FindIndex(Cases, row => row.Mode == _session.Rule.CaseMode));
        set
        {
            if (value >= 0 && value < Cases.Length)
            {
                Edit(CaseIndex, value, v => _session.Rule.CaseMode = Cases[v].Mode, nameof(CaseIndex));
            }
        }
    }

    public decimal? CounterStart
    {
        get => _session.Rule.CounterStart;
        set => EditNumber(_session.Rule.CounterStart, value, long.MinValue, long.MaxValue, v => _session.Rule.CounterStart = v, nameof(CounterStart));
    }

    public decimal? CounterStep
    {
        get => _session.Rule.CounterStep;
        set => EditNumber(_session.Rule.CounterStep, value, long.MinValue, long.MaxValue, v => _session.Rule.CounterStep = v, nameof(CounterStep));
    }

    public decimal? CounterDigits
    {
        get => _session.Rule.CounterDigits;
        set => EditNumber(_session.Rule.CounterDigits, value, 1, 12, v => _session.Rule.CounterDigits = (int)v, nameof(CounterDigits));
    }

    /// <summary>The case-mode names in the current language, in <see cref="CaseIndex"/> order.</summary>
    public IReadOnlyList<string> LocalizedCases() => Cases.Select(row => _text.Get(row.Key)).ToList();

    public void Draw()
    {
    }

    public Control CreateContent() => new RenameView(this, _text) { Name = Name };

    public void Load(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (IsBusy)
        {
            return;
        }

        _session = new RenameSession(files);
        _stamp = null;
        PublishPreview();
        ClearStatus();
        OnPropertyChanged(string.Empty);
        NotifyActions();
    }

    /// <summary>Replaces the selection (UTF-16 indices, as a text box reports them) with <paramref name="token"/>; returns the new caret in UTF-16 units.</summary>
    public int InsertToken(string token, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(token);
        var mask = _session.Rule.NameMask;
        var (text, caret) = RenameSession.InsertAt(mask, ToScalar(mask, start), ToScalar(mask, end), token);
        NameMask = text;
        return ToUtf16(text, caret);
    }

    private static int ToScalar(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        return text[..index].EnumerateRunes().Count();
    }

    private static int ToUtf16(string text, int scalars)
    {
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (scalars-- <= 0)
            {
                break;
            }

            length += rune.Utf16SequenceLength;
        }

        return length;
    }

    private void Edit<T>(T current, T value, Action<T> write, string property)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        write(value);
        _session.Refresh();
        ClearStatus();
        PublishPreview();
        OnPropertyChanged(property);
        NotifyActions();
    }

    private void EditNumber(long current, decimal? value, decimal min, decimal max, Action<long> write, string property)
    {
        if (value is null)
        {
            return;
        }

        var clamped = (long)Math.Clamp(Math.Truncate(value.Value), min, max);
        Edit(current, clamped, write, property);
    }

    private void NotifyActions()
    {
        UiThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanUndo));
            StartCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
        });
    }

    private void BeginBatch(bool forward)
    {
        if (IsBusy || (forward ? !_session.CanStart : !_session.CanUndo))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        NotifyActions();
        try
        {
            if (forward)
            {
                Watch(_session.Start(_jobs), "rename.renamed", _session.Previews.Count);
            }
            else
            {
                Watch(_session.Undo(_jobs), "rename.restored", _session.Done.Count);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _busy, 0);
            Remember(ex.Message, error: true);
            NotifyActions();
        }
    }

    private void Watch(IJob job, string key, int count) => _ = Finish(job, key, count);

    private async Task Finish(IJob job, string key, int count)
    {
        try
        {
            var result = await job.Completion.ConfigureAwait(false);
            if (result.Outcome == JobOutcome.Completed && result.Failed.Count == 0)
            {
                Remember(FormatCount(key, count), error: false);
                BatchChanged?.Invoke();
            }
            else
            {
                Remember(_session.Error, error: true);
            }
        }
        finally
        {
            // Idle on the UI thread, so the buttons and the status are current when IsBusy turns false.
            UiThread.Post(() =>
            {
                Interlocked.Exchange(ref _busy, 0);
                NotifyActions();
            });
        }
    }

    private void Remember(string? text, bool error)
    {
        var value = text ?? "";
        UiThread.Post(() =>
        {
            Status = value;
            StatusIsError = error && value.Length > 0;
        });
    }

    private void ClearStatus() => Remember(null, error: false);

    private string FormatCount(string key, int count) =>
        _text.Get(key).Replace("{n}", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private void PublishPreview()
    {
        var previews = _session.Previews;
        var stamp = Stamp(previews);
        if (_stamp is not null && string.Equals(stamp, _stamp, StringComparison.Ordinal))
        {
            return;
        }

        _stamp = stamp;
        var rows = new List<FileListRow>(previews.Count);
        foreach (var preview in previews)
        {
            rows.Add(Row(preview));
        }

        _list.ShowRows(Columns, rows);
    }

    private static readonly FileListColumn[] Columns =
    [
        new("rename.col.old", 200),
        new("rename.col.new", 200),
        new("error", 160, star: true),
    ];

    private static FileListRow Row(RenamePreview preview)
    {
        var oldName = NameOf(preview.From);
        var newName = NameOf(preview.To);
        return new FileListRow(preview.From, [oldName, newName, preview.Error ?? ""], oldName, false, false, false, preview);
    }

    private static string Stamp(IReadOnlyList<RenamePreview> previews)
    {
        var builder = new StringBuilder();
        foreach (var preview in previews)
        {
            builder.Append(preview.From).Append('\n').Append(preview.To).Append('\n').Append(preview.Error).Append('\n');
        }

        return builder.ToString();
    }

    private static string NameOf(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == 0 ? path : name;
    }
}
