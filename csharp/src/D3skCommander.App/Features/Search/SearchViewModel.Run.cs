using System.Collections.Concurrent;
using System.Globalization;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Shell;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Search;

namespace D3skCommander.App.Features.Search;

public sealed partial class SearchViewModel
{
    private const int MaxErrors = 50;
    private const int EventsPerDrain = 4000;

    private readonly ConcurrentQueue<(int Generation, SearchEvent Step)> _events = new();
    private readonly List<SearchHit> _hits = [];
    private readonly List<string> _errors = [];
    private IJob? _job;
    private int _generation;
    private long _scanned;
    private bool _dirty;
    private bool _duplicatesShown;
    private long _hashed;
    private long _hashTotal;
    private int _skipped;

    /// <summary>The hits shown so far, in the order of the rows.</summary>
    public IReadOnlyList<SearchHit> Hits => _hits;

    /// <summary>The job of the current or last search, so a test can wait for it.</summary>
    public IJob? Job => _job;

    public void Start()
    {
        var form = ReadForm();
        var result = SearchQueryBuilder.Build(form, DateTimeOffset.Now);
        if (result.Query is null)
        {
            Status = ProblemText(result);
            return;
        }

        Remember(form);

        _job?.Cancel();
        _generation++;
        var generation = _generation;
        _hits.Clear();
        _errors.Clear();
        _scanned = 0;
        _hashed = 0;
        _hashTotal = 0;
        _skipped = 0;
        _duplicatesShown = result.Query.Duplicates is not null;
        ErrorsLine = "";
        HitCount = 0;
        _results.ShowRows(_duplicatesShown ? SearchRows.DuplicateColumns : SearchRows.Columns, []);
        while (_events.TryDequeue(out _))
        {
        }

        IsRunning = true;
        ShowRunning();
        var query = result.Query;
        _job = _jobs.Start(_text.Get("cmd.search"), async (_, token) =>
        {
            await foreach (var step in _engine.RunAsync(query, token).ConfigureAwait(false))
            {
                _events.Enqueue((generation, step));
            }
        });
    }

    public void Cancel() => _job?.Cancel();

    /// <summary>Shows what the worker found since the last call. Runs on the UI thread.</summary>
    public void Drain()
    {
        var taken = 0;
        var finished = false;
        while (taken < EventsPerDrain && _events.TryDequeue(out var item))
        {
            taken++;
            if (item.Generation == _generation && Take(item.Step))
            {
                finished = true;
                break;
            }
        }

        if (!finished && _job is not null && !_job.IsRunning && _events.IsEmpty && IsRunning)
        {
            Finish(cancelled: true);
        }

        if (_dirty)
        {
            Publish();
        }

        if (IsRunning)
        {
            ShowRunning();
        }
    }

    /// <summary>Go to file: opens the folder of the row under the cursor in the active panel.</summary>
    public Task<bool> GoToCursorAsync()
    {
        var index = _results.CursorIndex;
        if (index < 0 || index >= _hits.Count)
        {
            return Task.FromResult(false);
        }

        return GoToAsync(_hits[index]);
    }

    public Task<bool> GoToAsync(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var folder = SearchRows.ParentOf(hit.Path);
        if (folder.Length == 0)
        {
            return Task.FromResult(false);
        }

        return _panels.GoAsync(_panels.ActiveSide, folder);
    }

    /// <summary>
    /// Opens the hits so far as a new tab of the active panel, named "Search: mask". ".." leaves it for
    /// the first search folder (or the panel's folder). The window stays open.
    /// </summary>
    public async Task<bool> FeedToPanelAsync()
    {
        if (_hits.Count == 0)
        {
            return false;
        }

        var side = _panels.ActiveSide;
        var roots = SearchQueryBuilder.SplitRoots(Roots);
        var start = roots.Count > 0 ? roots[0] : _panels.Read(side).Path;
        if (start.Length == 0)
        {
            return false;
        }

        var title = _text.Get("search.tab_title").Replace("{0}", NameMask, StringComparison.Ordinal);
        var paths = _hits.Select(hit => hit.Path).ToList();
        var groups = _duplicatesShown ? _hits.Select(hit => hit.Group).ToList() : null;
        return await _panels.OpenResultsAsync(side, start, title, paths, groups).ConfigureAwait(true);
    }

    private bool Take(SearchEvent step)
    {
        if (step.Kind == SearchEventKind.Hit && step.Hit is not null)
        {
            _hits.Add(step.Hit);
            _dirty = true;
        }
        else if (step.Kind == SearchEventKind.Scanned)
        {
            _scanned = step.Count;
        }
        else if (step.Kind == SearchEventKind.Hashing)
        {
            _hashed = step.Hashed;
            _hashTotal = step.Count;
        }
        else if (step.Kind == SearchEventKind.Error && step.Error is not null)
        {
            AddError(step.Error);
        }
        else if (step.Kind == SearchEventKind.Done)
        {
            _skipped = step.Skipped;
            Finish(step.Cancelled);
            return true;
        }

        return false;
    }

    private void AddError(Core.Common.OperationError error)
    {
        if (_errors.Count < MaxErrors)
        {
            _errors.Add(ErrorLines.Describe(_text, error));
        }

        ErrorsLine = _text.Get("search.errors")
            .Replace("{count}", _errors.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{first}", _errors[0], StringComparison.Ordinal);
    }

    private void Finish(bool cancelled)
    {
        IsRunning = false;
        var key = cancelled ? "search.status.cancelled" : "search.status.done";
        if (_duplicatesShown && !cancelled)
        {
            key = "search.status.done_duplicates";
        }

        var groups = _hits.Count == 0 ? 0 : _hits.Max(hit => hit.Group);
        Status = _text.Get(key)
            .Replace("{hits}", _hits.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{groups}", groups.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{skipped}", _skipped.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        _dirty = true;
    }

    private void Publish()
    {
        _dirty = false;
        var cursor = _results.CursorIndex;
        var rows = new List<FileListRow>(_hits.Count);
        foreach (var hit in _hits)
        {
            rows.Add(SearchRows.Row(hit, _text, _duplicatesShown));
        }

        _results.ShowRows(_duplicatesShown ? SearchRows.DuplicateColumns : SearchRows.Columns, rows);
        if (cursor > 0 && cursor < rows.Count)
        {
            _results.CursorIndex = cursor;
        }

        HitCount = _hits.Count;
    }

    private void ShowRunning()
    {
        if (_hashTotal > 0)
        {
            Status = _text.Get("search.status.hashing")
                .Replace("{done}", _hashed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{total}", _hashTotal.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            return;
        }

        ShowScanning();
    }

    private void ShowScanning() =>
        Status = _text.Get("search.status.running")
            .Replace("{scanned}", _scanned.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{hits}", _hits.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private string ProblemText(SearchQueryResult result)
    {
        var key = "search.problem.no_folder";
        if (result.Problem == SearchFormProblem.NoKind)
        {
            key = "search.problem.no_kind";
        }
        else if (result.Problem == SearchFormProblem.BadSize)
        {
            key = "search.problem.bad_size";
        }
        else if (result.Problem == SearchFormProblem.SizeRange)
        {
            key = "search.problem.size_range";
        }
        else if (result.Problem == SearchFormProblem.NoDuplicateCriterion)
        {
            key = "search.problem.no_duplicate";
        }
        else if (result.Problem == SearchFormProblem.BadDays)
        {
            key = "search.problem.bad_days";
        }

        return _text.Get(key).Replace("{text}", result.Detail, StringComparison.Ordinal);
    }
}
