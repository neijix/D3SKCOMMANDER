namespace D3skCommander.Core.Splitter;

/// <summary>A split job. <see cref="Parts"/> is filled when the job succeeds.</summary>
public sealed class SplitOperation : SplitterRun
{
    private string[] _parts = [];

    public IReadOnlyList<string> Parts => _parts;

    internal void SetParts(IReadOnlyList<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        _parts = parts as string[] ?? parts.ToArray();
    }
}
