namespace D3skCommander.Core.Jobs;

/// <summary>How a finished job ended. Failed items still finish as <see cref="Completed"/>.</summary>
public enum JobOutcome
{
    Completed,
    Cancelled,
}
