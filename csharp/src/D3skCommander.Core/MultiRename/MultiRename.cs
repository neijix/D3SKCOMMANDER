namespace D3skCommander.Core.MultiRename;

/// <summary>
/// Multi-rename engine from v0.1: masks, then search, then case; preview flags conflicts;
/// apply and undo move the whole batch or leave it untouched.
/// </summary>
public static partial class MultiRename
{
    /// <summary>The new file name (no directory) for <paramref name="file"/>, the 0-based <paramref name="index"/>.</summary>
    public static string NewName(string file, int index, RenameRule rule)
    {
        if (!TryNewName(file, index, rule, out var name, out var error))
        {
            throw new InvalidOperationException(error ?? "rename failed");
        }

        return name;
    }

    public static bool TryNewName(string file, int index, RenameRule rule, out string name, out string? error)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(rule.NameMask);
        ArgumentNullException.ThrowIfNull(rule.ExtMask);
        ArgumentNullException.ThrowIfNull(rule.Search);
        ArgumentNullException.ThrowIfNull(rule.Replace);
        name = "";
        var ctx = new Ctx(file, index, rule, StemOf(file), ExtensionOf(file));
        if (!TryExpand(rule.NameMask, ctx, out var stem, out error))
        {
            return false;
        }

        if (!TryExpand(rule.ExtMask, ctx, out var ext, out error))
        {
            return false;
        }

        var full = ext.Length == 0 ? stem : stem + "." + ext;
        if (!TrySearch(full, rule, out var replaced, out error))
        {
            return false;
        }

        name = ApplyCase(replaced, rule.CaseMode);
        return true;
    }

    /// <summary>
    /// New paths in the same folder. Invalid names, duplicate targets and names that already
    /// exist outside the batch are flagged; a name another file in the batch is leaving is not.
    /// </summary>
    public static IReadOnlyList<RenamePreview> Preview(IReadOnlyList<string> files, RenameRule rule)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(rule);
        var result = new RenamePreview[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            var from = files[i] ?? throw new ArgumentException("A file path is missing.", nameof(files));
            result[i] = BuildPreview(from, i, rule);
        }

        FlagConflicts(files, result);
        return result;
    }

    private static RenamePreview BuildPreview(string from, int index, RenameRule rule)
    {
        if (!TryNewName(from, index, rule, out var built, out var error))
        {
            return new RenamePreview(from, from, error);
        }

        return new RenamePreview(from, WithFileName(from, built), InvalidName(built));
    }

    private static void FlagConflicts(IReadOnlyList<string> files, RenamePreview[] result)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var preview in result)
        {
            if (preview.Error is not null)
            {
                continue;
            }

            var key = Key(preview.To);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (file is not null)
            {
                sources.Add(Key(file));
            }
        }

        foreach (var preview in result)
        {
            if (preview.Error is null)
            {
                FlagOne(preview, counts, sources);
            }
        }
    }

    private static void FlagOne(RenamePreview preview, Dictionary<string, int> counts, HashSet<string> sources)
    {
        if (counts.TryGetValue(Key(preview.To), out var count) && count > 1)
        {
            preview.Error = "another file in the batch gets the same name";
            return;
        }

        if (!PathsEqual(preview.To, preview.From)
            && !sources.Contains(Key(preview.To))
            && MetadataExists(preview.To)
            && !SameFile(preview.From, preview.To))
        {
            preview.Error = "a file with the new name already exists";
        }
    }

    private readonly struct Ctx
    {
        public Ctx(string file, int index, RenameRule rule, string stem, string ext)
        {
            File = file;
            Index = index;
            Rule = rule;
            Stem = stem;
            Ext = ext;
        }

        public string File { get; }

        public int Index { get; }

        public RenameRule Rule { get; }

        public string Stem { get; }

        public string Ext { get; }
    }
}
