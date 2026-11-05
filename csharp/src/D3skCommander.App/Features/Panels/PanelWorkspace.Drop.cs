using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

/// <summary>The hook for files dropped from the OS onto a pane; the drives feature sets it.</summary>
public interface IFileDrop
{
    Func<PanelSide, IReadOnlyList<string>, Task>? FilesDropped { get; set; }
}

public sealed partial class PanelWorkspace : IFileDrop
{
    public Func<PanelSide, IReadOnlyList<string>, Task>? FilesDropped { get; set; }

    /// <summary>Files from outside the app were dropped on <paramref name="side"/>: they are copied into its folder.</summary>
    public void DropFiles(PanelSide side, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var drop = FilesDropped;
        if (drop is not null && paths.Count > 0)
        {
            Run(() => drop(side, paths));
        }
    }
}
