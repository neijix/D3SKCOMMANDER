namespace D3skCommander.Core.Tests.Viewer;

internal sealed class ViewerTemp : IDisposable
{
    public ViewerTemp()
    {
        Path = Directory.CreateTempSubdirectory("d3sk-viewer-").FullName;
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
