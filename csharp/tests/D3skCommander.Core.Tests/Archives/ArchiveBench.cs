using D3skCommander.Core.Archives;
using Xunit;

namespace D3skCommander.Core.Tests.Archives;

internal sealed class Bench : IDisposable
{
    public Bench()
    {
        Root = Path.Combine(Path.GetTempPath(), "d3sk-arc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathOf(params string[] parts)
    {
        var path = Root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    public string MakeTree()
    {
        var proj = PathOf("proj");
        Directory.CreateDirectory(Path.Combine(proj, "docs", "sub"));
        Directory.CreateDirectory(Path.Combine(proj, "empty"));
        File.WriteAllText(Path.Combine(proj, "top.txt"), "top");
        var readme = Path.Combine(proj, "docs", "readme.txt");
        File.WriteAllText(readme, "hello readme");
        File.WriteAllText(Path.Combine(proj, "docs", "sub", ArchiveTests.UnicodeName), "unicode body");
        File.SetLastWriteTimeUtc(readme, DateTime.UnixEpoch.AddSeconds(ArchiveTests.ReadmeTime));
        return proj;
    }

    public string ArchivePath(ArchiveKind kind) => PathOf("out." + ArchiveKinds.Extension(kind));

    public BuiltArchive Built(ArchiveKind kind, int level)
    {
        var proj = MakeTree();
        var archive = ArchivePath(kind);
        var created = Arc.Create(archive, kind, new[] { proj }, level);
        AssertSuccess(created, kind);
        if (created.Value != 3)
        {
            throw new InvalidOperationException(kind + " stored " + created.Value);
        }

        return new BuiltArchive(archive);
    }

    public ArchiveEntry Find(IReadOnlyList<ArchiveEntry> entries, string path)
    {
        foreach (var entry in entries)
        {
            if (entry.Path == path)
            {
                return entry;
            }
        }

        throw new InvalidOperationException(path + " not listed");
    }

    public void AssertSlipRejected(string archive)
    {
        var dest = PathOf("out");
        var result = Arc.Extract(archive, null, dest);
        if (result.IsSuccess || result.Error is null)
        {
            throw new InvalidOperationException("slip was accepted");
        }

        if (result.Error.Kind != Core.Common.ErrorKind.InvalidData)
        {
            throw new InvalidOperationException(result.Error.Kind + " " + result.Error.Message);
        }

        if (!result.Error.Message.Contains("unsafe path", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(result.Error.Message);
        }

        if (File.Exists(PathOf("evil.txt")) || File.Exists(Path.Combine(dest, "evil.txt")))
        {
            throw new InvalidOperationException("evil.txt was written");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static void AssertSuccess(ArchiveResult<int> result, ArchiveKind kind)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(kind + " " + result.Error?.Message);
        }
    }
}

internal static class Arc
{
    public static ArchiveResult<int> Extract(
        string archive,
        IReadOnlyList<string>? entries,
        string destination,
        Func<string, bool>? progress = null) =>
        ArchiveIo.Extract(archive, entries, destination, progress, TestContext.Current.CancellationToken);

    public static ArchiveResult<int> Create(
        string archive,
        ArchiveKind kind,
        IReadOnlyList<string> sources,
        int level,
        Func<string, bool>? progress = null) =>
        ArchiveIo.Create(archive, kind, sources, level, progress, TestContext.Current.CancellationToken);
}

internal readonly struct BuiltArchive
{
    public BuiltArchive(string path) => Path = path;

    public string Path { get; }
}
