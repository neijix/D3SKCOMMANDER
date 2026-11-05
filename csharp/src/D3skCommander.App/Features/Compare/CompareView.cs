using System.Text;
using D3skCommander.App.Shell;
using D3skCommander.Core.Compare;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Compare;

/// <summary>Side-by-side compare: text rows, a binary peek, or the two editors.</summary>
public sealed partial class CompareView : IShellWidget
{
    private readonly IJobService _jobs;
    private readonly ILocalizer _text;
    private readonly Action? _saved;
    private readonly StringBuilder _rows = new();
    private CompareSession _session;
    private string _leftPath;
    private string _rightPath;
    private string _leftEdit = "";
    private string _rightEdit = "";
    private CompareBuffer? _leftSource;
    private CompareBuffer? _rightSource;
    private bool _started;
    private bool _forceView;

    public CompareView(string left, string right, IJobService jobs, ILocalizer text, Action? saved)
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

    public string? Name => "CompareFiles";

    public CompareSession Session => _session;

    public CompareSurface Surface { get; private set; }

    public string SurfaceText { get; private set; } = "";

    public string StatusLine { get; private set; } = "";

    public string DrawnRows { get; private set; } = "";

    public bool DrewNext { get; private set; }

    public bool DrewSave { get; private set; }

    public bool DrewPeek { get; private set; }

    public void Restart(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _leftPath = left;
        _rightPath = right;
        _forceView = false;
        _leftSource = null;
        _rightSource = null;
        _started = true;
        _session = OpenSession(left, right);
        _session.CompareFiles();
    }

    public void NextBlock() => _session.GoToDifference(true);

    public void PreviousBlock() => _session.GoToDifference(false);

    public void BeginEdit()
    {
        _session.EnterEdit();
        _forceView = _session.Mode != CompareMode.Edit;
    }

    public void ShowCompareView() => _forceView = true;

    public void Draw()
    {
        DrewNext = false;
        DrewSave = false;
        DrewPeek = false;
        DrawnRows = "";
        EnsureStarted();
        _session.Poll();
        DrawPaths();
        DrawNavigation();
        DrawOptions();
        DrawModes();
        DrawStatus();
        ImGui.Separator();
        DrawBody();
    }

    private bool ShowingEdit => _session.Mode == CompareMode.Edit && !_forceView;

    private CompareSession OpenSession(string left, string right)
    {
        var title = _text.Get("tool.compare.title");
        var session = new CompareSession(left, right, new CompareJobTitles(_jobs, title));
        session.ReloadRequested += OnSaved;
        return session;
    }

    private void OnSaved() => _saved?.Invoke();

    private void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _session.CompareFiles();
    }

    private void DrawBody()
    {
        if (ShowingEdit)
        {
            Surface = CompareSurface.Edit;
            DrawEditors();
            return;
        }

        var outcome = _session.Outcome;
        if (outcome.Kind == CompareKind.Error)
        {
            DrawError(outcome.Error);
            return;
        }

        if (outcome.Kind == CompareKind.Binary)
        {
            Surface = CompareSurface.Binary;
            DrawBinary(outcome);
            return;
        }

        if (outcome.Kind == CompareKind.Text)
        {
            Surface = CompareSurface.Text;
            DrawText(outcome);
            return;
        }

        Surface = _session.HasPendingResult ? CompareSurface.Pending : CompareSurface.Empty;
        SurfaceText = Surface == CompareSurface.Pending ? _text.Get("compare.working") : "";
        if (SurfaceText.Length > 0)
        {
            ImGui.TextUnformatted(SurfaceText);
        }
    }

    private void DrawError(string? detail)
    {
        Surface = CompareSurface.Error;
        var message = string.IsNullOrEmpty(detail) ? _text.Get("error.other") : detail;
        SurfaceText = CompareText.Status(_text, CompareStatus.Error(message));
        ImGui.TextWrapped(SurfaceText);
    }
}
