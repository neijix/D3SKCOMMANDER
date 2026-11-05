using D3skCommander.Core.Common;

namespace D3skCommander.Core.Sync;

/// <summary>What applying a plan did. Item errors are collected; they do not stop the rest.</summary>
public sealed class SyncReport
{
    private readonly List<OperationError> _errors = [];

    public int Copied { get; internal set; }

    public int Deleted { get; internal set; }

    public IReadOnlyList<OperationError> Errors => _errors;

    internal void Add(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _errors.Add(error);
    }
}
