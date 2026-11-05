using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Tests.Providers;

public sealed class ListProviderFixture : ProviderFixture
{
    private readonly string _root;

    public ListProviderFixture()
    {
        _root = System.IO.Directory.CreateTempSubdirectory("d3sk-list-").FullName;
        Directory = Path.Combine(_root, "folder");
        System.IO.Directory.CreateDirectory(Directory);
        SampleBytes = [1, 2, 3, 4, 5];
        FileNames = ["a.txt", "b.txt", "c.txt"];
        var entries = new List<Entry>();
        foreach (var name in FileNames)
        {
            var path = Path.Combine(Directory, name);
            File.WriteAllBytes(path, SampleBytes);
            entries.Add(Read(path));
        }

        var hidden = Path.Combine(Directory, ".hidden");
        File.WriteAllBytes(hidden, SampleBytes);
        entries.Add(Read(hidden));
        SampleFileName = FileNames[0];
        SampleFilePath = Combine(Directory, SampleFileName);
        Provider = new ListFileSystemProvider(Directory, entries);
    }

    public override IFileSystemProvider Provider { get; }

    public override string Directory { get; }

    public override IReadOnlyList<string> FileNames { get; }

    public override string SampleFileName { get; }

    public override string SampleFilePath { get; }

    public override byte[] SampleBytes { get; }

    public override string Combine(string directory, string name) => Path.Combine(directory, name);

    public override void Dispose() => System.IO.Directory.Delete(_root, recursive: true);

    private static Entry Read(string path)
    {
        var read = LocalFileSystem.ReadEntry(path);
        if (!read.IsSuccess)
        {
            throw new InvalidOperationException(read.Error?.Message ?? "the sample file was not read");
        }

        return read.Value;
    }
}
