namespace D3skCommander.Core.Tests.Compare;

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = Directory.CreateTempSubdirectory("d3sk-compare-").FullName;
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
