namespace D3skCommander.Core.MultiRename;

public static partial class MultiRename
{
    /// <summary>
    /// Renames every preview that changes a name. Refuses the whole batch when one has an error.
    /// A source that is also a target goes through a temporary name, so swaps and chains work.
    /// On failure, everything done is rolled back.
    /// </summary>
    public static RenameOutcome Apply(IReadOnlyList<RenamePreview> previews)
    {
        ArgumentNullException.ThrowIfNull(previews);
        foreach (var preview in previews)
        {
            if (preview is null)
            {
                throw new ArgumentException("A preview is missing.", nameof(previews));
            }

            if (preview.Error is not null)
            {
                return RenameOutcome.Fail(preview.From + ": " + preview.Error);
            }
        }

        var pairs = new RenamePair[previews.Count];
        for (var i = 0; i < previews.Count; i++)
        {
            pairs[i] = new RenamePair(previews[i].From, previews[i].To);
        }

        return Execute(pairs);
    }

    /// <summary>Reverses a previous <see cref="Apply"/>. Null when every name is back.</summary>
    public static string? Undo(IReadOnlyList<RenamePair> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var reversed = new RenamePair[done.Count];
        for (var i = 0; i < done.Count; i++)
        {
            var pair = done[i] ?? throw new ArgumentException("A rename pair is missing.", nameof(done));
            reversed[i] = new RenamePair(pair.To, pair.From);
        }

        return Execute(reversed).Error;
    }

    private static RenameOutcome Execute(IReadOnlyList<RenamePair> pairs)
    {
        var moving = new List<RenamePair>();
        foreach (var pair in pairs)
        {
            if (!PathsEqual(pair.From, pair.To))
            {
                moving.Add(pair);
            }
        }

        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in moving)
        {
            targets.Add(pair.To);
        }

        var log = new List<RenamePair>();
        var error = MoveAll(moving, targets, log);
        if (error is null)
        {
            return RenameOutcome.Ok(moving);
        }

        return RenameOutcome.Fail(error + RollbackSuffix(Rollback(log)));
    }

    private static string? MoveAll(List<RenamePair> moving, HashSet<string> targets, List<RenamePair> log)
    {
        var direct = new List<RenamePair>();
        var staged = new List<RenamePair>();
        for (var n = 0; n < moving.Count; n++)
        {
            var pair = moving[n];
            if (!targets.Contains(pair.From))
            {
                direct.Add(pair);
                continue;
            }

            var temp = TempName(pair.From, n);
            var stagedError = RenameChecked(pair.From, temp, log);
            if (stagedError is not null)
            {
                return stagedError;
            }

            staged.Add(new RenamePair(temp, pair.To));
        }

        foreach (var pair in direct)
        {
            var error = RenameChecked(pair.From, pair.To, log);
            if (error is not null)
            {
                return error;
            }
        }

        foreach (var pair in staged)
        {
            var error = RenameChecked(pair.From, pair.To, log);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private static string RollbackSuffix(List<string> stuck)
    {
        if (stuck.Count == 0)
        {
            return " (rolled back)";
        }

        return " (rollback failed for: " + string.Join(", ", stuck) + ")";
    }

    private static string? RenameChecked(string from, string to, List<RenamePair> log)
    {
        if (MetadataExists(to) && !SameFile(from, to))
        {
            return to + " already exists";
        }

        try
        {
            MoveEntry(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "cannot rename " + from + " to " + to + ": " + ex.Message;
        }

        log.Add(new RenamePair(from, to));
        return null;
    }

    private static string TempName(string path, int n)
    {
        var name = Path.GetFileName(path);
        var k = n;
        while (true)
        {
            var candidate = WithFileName(path, "." + name + ".d3sk-tmp-" + k.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!MetadataExists(candidate))
            {
                return candidate;
            }

            k++;
        }
    }

    private static List<string> Rollback(List<RenamePair> log)
    {
        var stuck = new List<string>();
        for (var i = log.Count - 1; i >= 0; i--)
        {
            var pair = log[i];
            try
            {
                MoveEntry(pair.To, pair.From);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stuck.Add(pair.From);
            }
        }

        return stuck;
    }

    private static void MoveEntry(string from, string to)
    {
        var attributes = File.GetAttributes(from);
        if (attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Directory.Move(from, to);
            return;
        }

        File.Move(from, to);
    }
}
