namespace D3skCommander.Core.Splitter;

/// <summary>
/// A combine job. <see cref="Report"/> is set when the parts were joined,
/// including when the CRC check failed. A failed check keeps the output file.
/// </summary>
public sealed class CombineOperation : SplitterRun
{
    public CombineReport? Report { get; private set; }

    internal void SetReport(CombineReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
    }
}
