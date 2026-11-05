using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.FileOps;

/// <summary>
/// Overwrite, skip, rename, or overwrite-older. An "all" choice is remembered for the
/// rest of the job. A reply that ends without a choice cancels, as a dropped channel did.
/// </summary>
internal sealed class ConflictPolicy
{
    private enum Remembered
    {
        None,
        Overwrite,
        Skip,
        Older,
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
        var question = Describe(source, sourceEntry, target, targetEntry);
        if (_policy == Remembered.Overwrite)
        {
            return ConflictDecision.WriteTo(target);
        }

        if (_policy == Remembered.Skip)
        {
            return ConflictDecision.SkipIt();
        }

        if (_policy == Remembered.Older)
        {
            return Older(question, target);
        }

        if (work.Ask is null)
        {
            throw new InvalidOperationException("unexpected conflict");
        }

        work.Session.AddConflict(question);
        var choice = await ReadChoiceAsync(work, question, cancellationToken).ConfigureAwait(false);
        return Apply(choice, question, target);
    }

    private static async Task<ConflictChoice> ReadChoiceAsync(
        TransferWork work,
        ConflictQuestion question,
        CancellationToken cancellationToken)
    {
        try
        {
            return await work.Ask!(question, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ConflictChoice.Cancel;
        }
    }

    private ConflictDecision Apply(ConflictChoice choice, ConflictQuestion question, string target)
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

        if (choice == ConflictChoice.OverwriteAllOlder)
        {
            _policy = Remembered.Older;
            return Older(question, target);
        }

        if (choice == ConflictChoice.RenameNew)
        {
            return ConflictDecision.WriteTo(Renamed(target));
        }

        return ConflictDecision.CancelIt();
    }

    /// <summary>Replace the target only when its date is strictly older than the source.</summary>
    private static ConflictDecision Older(ConflictQuestion question, string target)
    {
        if (question.SourceModified is { } source
            && question.TargetModified is { } existing
            && existing < source)
        {
            return ConflictDecision.WriteTo(target);
        }

        return ConflictDecision.SkipIt();
    }

    private static string Renamed(string target)
    {
        var directory = Path.GetDirectoryName(target);
        var name = Path.GetFileName(target);
        if (string.IsNullOrEmpty(directory) || name.Length == 0)
        {
            return target;
        }

        return FileOpNames.UniqueName(directory, name);
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
