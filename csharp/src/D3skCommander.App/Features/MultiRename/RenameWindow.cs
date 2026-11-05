using System.Globalization;
using System.Numerics;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Shell;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.MultiRename;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.MultiRename;

/// <summary>Where a token button was drawn, so a test can click it.</summary>
public readonly record struct RenameTokenHit(string Token, Vector2 Center);

/// <summary>
/// Multi-rename tool: masks, search, case and a live preview. The rules live in
/// <see cref="RenameSession"/>; this window only edits them and draws.
/// </summary>
public sealed partial class RenameWindow : IShellWidget
{
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly FileList _list;
    private readonly ImGuiInputTextCallback _onMask;
    private readonly List<RenameTokenHit> _hits = [];
    private readonly object _statusGate = new();
    private RenameSession _session;
    private string _seenMask = "";
    private string? _statusText;
    private int _nameStart;
    private int _nameEnd;
    private int _maskEpoch;
    private int _busy;
    private string? _stamp;
    private bool _reloadMask = true;
    private bool _statusIsError;

    public RenameWindow(ILocalizer text, IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        _text = text;
        _jobs = jobs;
        unsafe
        {
            _onMask = OnNameMask;
        }

        _list = new FileList(text);
        _session = new RenameSession([]);
        PlaceCaret(_session.Rule.NameMask);
    }

    public string? Name => "MultiRename";

    public RenameSession Session => _session;

    public FileList Preview => _list;

    public IReadOnlyList<RenameTokenHit> TokenButtons => _hits;

    public Vector2 StartCenter { get; private set; }

    public Vector2 UndoCenter { get; private set; }

    public bool StartEnabled { get; private set; }

    public bool UndoEnabled { get; private set; }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>Raised on the worker after Start or Undo renames the batch, so the panels can reload.</summary>
    public event Action? BatchChanged;

    public void Load(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (IsBusy)
        {
            return;
        }

        _session = new RenameSession(files);
        _stamp = null;
        _reloadMask = true;
        PlaceCaret(_session.Rule.NameMask);
        ClearStatus();
    }

    public void Draw()
    {
        ImGui.PushID("multirename");
        try
        {
            DrawForm();
            ImGui.Separator();
            DrawPreview();
            ImGui.Separator();
            DrawActions();
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private void Insert(string token)
    {
        var mask = _session.Rule.NameMask;
        var (text, caret) = RenameSession.InsertAt(mask, _nameStart, _nameEnd, token);
        _session.Rule.NameMask = text;
        _nameStart = caret;
        _nameEnd = caret;
        Changed();
    }

    private void Changed()
    {
        _session.Refresh();
        ClearStatus();
    }

    private void PlaceCaret(string text)
    {
        var count = text.EnumerateRunes().Count();
        _nameStart = count;
        _nameEnd = count;
    }

    private unsafe int OnNameMask(ImGuiInputTextCallbackData* data)
    {
        _nameStart = data->SelectionStart;
        _nameEnd = data->SelectionEnd;
        return 0;
    }

    private void BeginStart() => BeginBatch(true);

    private void BeginUndo() => BeginBatch(false);

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
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Remember(string? text, bool error)
    {
        lock (_statusGate)
        {
            _statusText = text;
            _statusIsError = error && !string.IsNullOrEmpty(text);
        }
    }

    private void ClearStatus() => Remember(null, error: false);

    private string? ReadStatus(out bool error)
    {
        lock (_statusGate)
        {
            error = _statusIsError;
            return _statusText;
        }
    }

    private string FormatCount(string key, int count) =>
        _text.Get(key).Replace("{n}", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
