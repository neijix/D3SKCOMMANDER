namespace D3skCommander.Core.Common;

/// <summary>One failed item in a job report: the item, its typed error, and whether Retry applies.</summary>
public sealed class ItemReport
{
    public ItemReport(string item, OperationError error, bool retryable)
    {
        ArgumentException.ThrowIfNullOrEmpty(item);
        ArgumentNullException.ThrowIfNull(error);
        Item = item;
        Error = error;
        Retryable = retryable;
    }

    public string Item { get; }

    public OperationError Error { get; }

    public bool Retryable { get; }
}
