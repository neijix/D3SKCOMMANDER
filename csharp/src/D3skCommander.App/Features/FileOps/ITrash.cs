using D3skCommander.Core.Trash;

namespace D3skCommander.App.Features.FileOps;

/// <summary>The OS trash behind an interface, so tests never touch the real one.</summary>
public interface ITrash
{
    TrashStatus MoveToTrash(string path);
}

/// <summary>The real OS trash (<see cref="OsTrash"/>).</summary>
public sealed class SystemTrash : ITrash
{
    public TrashStatus MoveToTrash(string path) => OsTrash.MoveToTrash(path);
}
