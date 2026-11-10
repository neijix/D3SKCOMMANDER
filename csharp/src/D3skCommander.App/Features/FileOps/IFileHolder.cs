namespace D3skCommander.App.Features.FileOps;

/// <summary>
/// A part of the app that may hold a file open (the preview playing a video). Before a delete, each holder lets go
/// of any of the paths (or a file under one of their folders) and returns once the file is closed.
/// </summary>
public interface IFileHolder
{
    Task LetGoAsync(IReadOnlyList<string> paths);
}
