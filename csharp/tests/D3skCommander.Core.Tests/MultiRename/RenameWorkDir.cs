namespace D3skCommander.Core.Tests.MultiRename;

internal sealed class RenameWorkDir : IDisposable
{
    public RenameWorkDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "d3sk-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Touch(string name)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, name);
        return path;
    }

    public string PathOf(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
