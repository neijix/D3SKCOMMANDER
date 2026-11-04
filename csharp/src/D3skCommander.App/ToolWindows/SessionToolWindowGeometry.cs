namespace D3skCommander.App.ToolWindows;

/// <summary>Process-lifetime geometry. Nothing here touches the file system.</summary>
public sealed class SessionToolWindowGeometry : IToolWindowGeometry
{
    private readonly Dictionary<string, ToolWindowFrame> _frames = new(StringComparer.Ordinal);

    public bool TryRecall(ToolKind kind, out ToolWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return _frames.TryGetValue(kind.Id, out frame);
    }

    public void Remember(ToolKind kind, ToolWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(kind);
        _frames[kind.Id] = frame;
    }
}
