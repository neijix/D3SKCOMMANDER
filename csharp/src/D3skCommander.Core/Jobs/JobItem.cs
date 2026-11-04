namespace D3skCommander.Core.Jobs;

/// <summary>
/// One unit of work. <see cref="Name"/> is the retry key and the failed-item text.
/// <see cref="Bytes"/> is the declared size; zero means the work reports bytes itself.
/// </summary>
public sealed class JobItem
{
    public JobItem(string name, long bytes = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Name = name;
        Bytes = bytes;
    }

    public string Name { get; }

    public long Bytes { get; }
}
