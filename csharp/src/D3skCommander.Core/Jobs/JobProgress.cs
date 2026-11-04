namespace D3skCommander.Core.Jobs;

/// <summary>Items, bytes and the name currently being worked on.</summary>
public readonly record struct JobProgress(
    int ItemsDone,
    int ItemsTotal,
    long BytesDone,
    long BytesTotal,
    string CurrentName)
{
    public static JobProgress Empty { get; } = new(0, 0, 0, 0, "");
}
