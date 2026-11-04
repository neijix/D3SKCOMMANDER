using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.FileOps;

/// <summary>
/// Overwrite, skip, or cancel, remembered for the rest of the job when the choice says all.
/// </summary>
internal sealed class ConflictPolicy
{
    private enum Remembered
    {
        None,
        Overwrite,
        Skip,
    }

    private Remembered _policy;

    public void Reset() => _policy = Remembered.None;

    public async Task<ConflictDecision> DecideAsync(
        TransferWork work,
        string source,
        Entry sourceEntry,
        string target,
        Entry targetEntry,
        CancellationToken cancellationToken)
    {
        if (_policy == Remembered.Overwrite)
        {
            return ConflictDecision.WriteTo(target);
        }

        if (_policy == Remembered.Skip)
        {
            return ConflictDecision.SkipIt();
        }

        if (work.Ask is null)
        {
            throw new InvalidOperationException("unexpected conflict");
        }

        var question = Describe(source, sourceEntry, target, targetEntry);
        work.Session.AddConflict(question);
        var choice = await work.Ask(question, cancellationToken).ConfigureAwait(false);
        return Remember(choice, target);
    }

    private ConflictDecision Remember(ConflictChoice choice, string target)
    {
        if (choice == ConflictChoice.Overwrite)
        {
            return ConflictDecision.WriteTo(target);
        }

        if (choice == ConflictChoice.Skip)
        {
            return ConflictDecision.SkipIt();
        }

        if (choice == ConflictChoice.OverwriteAll)
        {
            _policy = Remembered.Overwrite;
            return ConflictDecision.WriteTo(target);
        }

        if (choice == ConflictChoice.SkipAll)
        {
            _policy = Remembered.Skip;
            return ConflictDecision.SkipIt();
        }

        return ConflictDecision.CancelIt();
    }

    private static ConflictQuestion Describe(string source, Entry sourceEntry, string target, Entry targetEntry)
    {
        return new ConflictQuestion(
            source,
            Length(sourceEntry),
            sourceEntry.Modified,
            target,
            Length(targetEntry),
            targetEntry.Modified);
    }

    private static long Length(Entry entry) =>
        entry.IsLink ? 0 : TreeCount.ToLong(entry.Size);
}
