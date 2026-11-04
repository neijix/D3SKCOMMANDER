namespace D3skCommander.App.ToolWindows;

/// <summary>
/// Where each tool window last sat. This session copy is not written to disk;
/// the settings row persists it in the session store.
/// </summary>
public interface IToolWindowGeometry
{
    bool TryRecall(ToolKind kind, out ToolWindowFrame frame);

    void Remember(ToolKind kind, ToolWindowFrame frame);
}
