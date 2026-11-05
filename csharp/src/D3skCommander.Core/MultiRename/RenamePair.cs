namespace D3skCommander.Core.MultiRename;

/// <summary>One rename that <see cref="MultiRename.Apply"/> performed, in batch order.</summary>
public sealed class RenamePair
{
    public RenamePair(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
    }

    public string From { get; }

    public string To { get; }
}
