namespace D3skCommander.Core.Tests.Search;

internal sealed class SearchTempDir : IDisposable
{
    public SearchTempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "d3sk-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Write(string relative, string text) => Write(relative, System.Text.Encoding.UTF8.GetBytes(text));

    public string Write(string relative, byte[] data)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
