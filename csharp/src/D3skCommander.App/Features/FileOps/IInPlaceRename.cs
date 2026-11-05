namespace D3skCommander.App.Features.FileOps;

/// <summary>
/// Implemented by the panel workspace: <see cref="Begin"/> opens the active list's name editor;
/// when the user commits a name, the workspace awaits <see cref="Commit"/> with the entry's full
/// path and the new name. FileOpsCommands sets <see cref="Commit"/> when it binds.
/// </summary>
public interface IInPlaceRename
{
    Func<string, string, Task>? Commit { get; set; }

    void Begin();
}
