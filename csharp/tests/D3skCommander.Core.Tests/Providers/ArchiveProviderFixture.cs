using D3skCommander.Core.Archives;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Tests.Providers;

public sealed class ArchiveProviderFixture : ProviderFixture
{
    private readonly string _root;

    public ArchiveProviderFixture()
    {
        _root = System.IO.Directory.CreateTempSubdirectory("d3sk-archive-").FullName;
        var source = Path.Combine(_root, "folder");
        System.IO.Directory.CreateDirectory(source);
        SampleBytes = [1, 2, 3, 4, 5];
        FileNames = ["a.txt", "b.txt", "c.txt"];
        foreach (var name in FileNames)
        {
            File.WriteAllBytes(Path.Combine(source, name), SampleBytes);
        }

        File.WriteAllBytes(Path.Combine(source, ".hidden"), SampleBytes);
        ArchivePath = Path.Combine(_root, "sample.zip");
        var created = ArchiveIo.Create(ArchivePath, ArchiveKind.Zip, [source], 0);
        if (!created.IsSuccess)
        {
            throw new InvalidOperationException(created.Error?.Message ?? "the sample archive was not created");
        }

        Directory = Path.Combine(ArchivePath, "folder");
        SampleFileName = FileNames[0];
        SampleFilePath = Combine(Directory, SampleFileName);
        Provider = new ArchiveFileSystemProvider();
    }

    public string ArchivePath { get; }

    public override IFileSystemProvider Provider { get; }

    public override string Directory { get; }

    public override IReadOnlyList<string> FileNames { get; }

    public override string SampleFileName { get; }

    public override string SampleFilePath { get; }

    public override byte[] SampleBytes { get; }

    public override string Combine(string directory, string name) => Path.Combine(directory, name);

    public override void Dispose() => System.IO.Directory.Delete(_root, recursive: true);
}
