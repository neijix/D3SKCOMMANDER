namespace D3skCommander.Core.OperationLog;

/// <summary>
/// One line per finished operation, in the v0.1 shape
/// <c>yyyy-MM-dd HH:mm:ss\tOP\tsrc\tdst\tOK|ERROR msg</c>.
/// </summary>
public interface IOperationLog
{
    /// <summary>When false, <see cref="Record"/> writes nothing. Off until settings turn it on.</summary>
    bool Enabled { get; set; }

    /// <summary>
    /// Appends one line. <paramref name="error"/> null means OK. Never throws:
    /// a failed write must not fail the operation.
    /// </summary>
    void Record(string operation, string source, string? destination, string? error);

    /// <summary>
    /// Switches the sink to <c>ops.log</c> in <paramref name="settingsFolder"/>.
    /// Lines still only in memory are appended there first.
    /// </summary>
    void UseSettingsFolder(string settingsFolder);

    /// <summary>Lines so far, in order. From memory, or from the file once a folder is set.</summary>
    IReadOnlyList<string> ReadLines();
}
