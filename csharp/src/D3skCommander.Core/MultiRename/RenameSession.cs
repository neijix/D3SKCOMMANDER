using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.MultiRename;

/// <summary>
/// The multi-rename session: live preview, insert at the caret, and Start / Undo.
/// Each of Start and Undo is one job for the whole batch. The panel paths follow the rename and its undo.
/// </summary>
public sealed class RenameSession
{
    private readonly object _gate = new();
    private readonly List<string> _files;
    private readonly List<RenamePair> _done = [];
    private List<RenamePreview> _previews;

    public RenameSession(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = [];
        foreach (var file in files)
        {
            _files.Add(file ?? throw new ArgumentException("A file path is missing.", nameof(files)));
        }

        Rule = new RenameRule();
        _previews = [];
        RefreshUnlocked();
    }

    public RenameRule Rule { get; }

    public IReadOnlyList<string> Files
    {
        get
        {
            lock (_gate)
            {
                return _files.ToArray();
            }
        }
    }

    public IReadOnlyList<RenamePreview> Previews
    {
        get
        {
            lock (_gate)
            {
                return _previews.ToArray();
            }
        }
    }

    public IReadOnlyList<RenamePair> Done
    {
        get
        {
            lock (_gate)
            {
                return _done.ToArray();
            }
        }
    }

    /// <summary>Engine error from the last Start or Undo, or null after a success.</summary>
    public string? Error
    {
        get
        {
            lock (_gate)
            {
                return _error;
            }
        }
    }

    private string? _error;

    public bool HasError
    {
        get
        {
            lock (_gate)
            {
                return HasErrorUnlocked();
            }
        }
    }

    /// <summary>v0.1 enables Start only when the preview is non-empty and has no error.</summary>
    public bool CanStart
    {
        get
        {
            lock (_gate)
            {
                return _previews.Count > 0 && !HasErrorUnlocked();
            }
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _done.Count > 0;
            }
        }
    }

    public void Refresh()
    {
        lock (_gate)
        {
            RefreshUnlocked();
        }
    }

    /// <summary>Applies the current preview as one job. A preview error renames nothing.</summary>
    public IJob Start(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        string[] files;
        RenamePreview[] previews;
        lock (_gate)
        {
            files = _files.ToArray();
            previews = _previews.ToArray();
        }

        return jobs.Start(
            "Multi-rename",
            [new JobItem("Multi-rename")],
            (_, _, token) => Task.FromResult(RunApply(files, previews, token)));
    }

    /// <summary>Undoes the last Start as one job.</summary>
    public IJob Undo(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        string[] files;
        RenamePair[] done;
        lock (_gate)
        {
            files = _files.ToArray();
            done = _done.ToArray();
        }

        return jobs.Start(
            "Undo rename",
            [new JobItem("Undo rename")],
            (_, _, token) => Task.FromResult(RunUndo(files, done, token)));
    }

    /// <summary>
    /// Replaces chars <paramref name="start"/>..<paramref name="end"/> (a caret when equal) with
    /// <paramref name="token"/>. Indices count Unicode scalars and are clamped. Returns the text
    /// and the scalar index just after the token.
    /// </summary>
    public static (string Text, int Caret) InsertAt(string text, int start, int end, string token)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(token);
        var runes = text.EnumerateRunes().ToArray();
        var length = runes.Length;
        var from = Math.Clamp(Math.Min(start, end), 0, length);
        var to = Math.Clamp(Math.Max(start, end), 0, length);
        var output = new System.Text.StringBuilder(text.Length + token.Length);
        for (var i = 0; i < from; i++)
        {
            output.Append(runes[i]);
        }

        output.Append(token);
        for (var i = to; i < length; i++)
        {
            output.Append(runes[i]);
        }

        return (output.ToString(), from + token.EnumerateRunes().Count());
    }

    /// <summary>
    /// Follows <paramref name="files"/> through a rename (<paramref name="forward"/>) or its undo.
    /// A path that one pair moved becomes the other side of that pair.
    /// </summary>
    public static IReadOnlyList<string> Remap(IReadOnlyList<string> files, IReadOnlyList<RenamePair> done, bool forward)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(done);
        var result = new string[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i] ?? throw new ArgumentException("A file path is missing.", nameof(files));
            result[i] = MapOne(file, done, forward);
        }

        return result;
    }

    private static string MapOne(string file, IReadOnlyList<RenamePair> done, bool forward)
    {
        foreach (var pair in done)
        {
            if (pair is null)
            {
                continue;
            }

            if (forward && pair.From == file)
            {
                return pair.To;
            }

            if (!forward && pair.To == file)
            {
                return pair.From;
            }
        }

        return file;
    }

    private ItemResult RunApply(string[] files, RenamePreview[] previews, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return FinishApply(files, MultiRename.Apply(previews));
    }

    private ItemResult RunUndo(string[] files, RenamePair[] done, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var error = MultiRename.Undo(done);
        lock (_gate)
        {
            if (error is not null)
            {
                _error = error;
                return ItemResult.Failed(OperationError.FromMessage(error), retryable: false);
            }

            Replace(_files, Remap(files, done, forward: false));
            _done.Clear();
            _error = null;
            RefreshUnlocked();
            return ItemResult.Success;
        }
    }

    private ItemResult FinishApply(IReadOnlyList<string> files, RenameOutcome outcome)
    {
        lock (_gate)
        {
            if (outcome.Error is not null)
            {
                _error = outcome.Error;
                return ItemResult.Failed(OperationError.FromMessage(outcome.Error), retryable: false);
            }

            Replace(_files, Remap(files, outcome.Done, forward: true));
            _done.Clear();
            foreach (var pair in outcome.Done)
            {
                _done.Add(pair);
            }

            _error = null;
            RefreshUnlocked();
            return ItemResult.Success;
        }
    }

    private void RefreshUnlocked()
    {
        var built = MultiRename.Preview(_files, Rule);
        _previews = [];
        foreach (var preview in built)
        {
            _previews.Add(preview);
        }
    }

    private bool HasErrorUnlocked()
    {
        foreach (var preview in _previews)
        {
            if (preview.Error is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static void Replace(List<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
