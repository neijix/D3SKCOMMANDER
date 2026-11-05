namespace D3skCommander.Core.Tests.Update;

internal sealed class WorkDir : IDisposable
{
    public WorkDir()
    {
        Path = Directory.CreateTempSubdirectory("d3sk-update-").FullName;
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file the test still has open is removed by the OS later.
        }
    }
}
