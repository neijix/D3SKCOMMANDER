using D3skCommander.Core.Common;

namespace D3skCommander.Core.Jobs;

/// <summary>
/// Typed outcome of a job. <see cref="Failed"/> lists each failed item;
/// <see cref="JobOutcome.Cancelled"/> is the whole job stopping, not one item's error.
/// </summary>
public sealed class JobResult
{
    public JobResult(JobOutcome outcome, IReadOnlyList<ItemReport> failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        Outcome = outcome;
        Failed = Copy(failed);
    }

    public JobOutcome Outcome { get; }

    public IReadOnlyList<ItemReport> Failed { get; }

    private static ItemReport[] Copy(IReadOnlyList<ItemReport> failed)
    {
        var copy = new ItemReport[failed.Count];
        for (var i = 0; i < failed.Count; i++)
        {
            var report = failed[i];
            if (report is null)
            {
                throw new ArgumentException("A failed item is missing.", nameof(failed));
            }

            copy[i] = report;
        }

        return copy;
    }
}
