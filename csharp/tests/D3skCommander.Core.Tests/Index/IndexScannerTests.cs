using D3skCommander.Core.Index;
using Xunit;
using static D3skCommander.Core.Tests.Index.IndexTree;

namespace D3skCommander.Core.Tests.Index;

public sealed class IndexScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "d3sk-idx-scan-" + Guid.NewGuid().ToString("N"));

    public IndexScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ListsEveryFileAndFolderWithSizeAndTime()
    {
        Write("a.txt", 10);
        Write("sub/b.bin", 300);
        Write("sub/deep/c.md", 5);
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var index = Build([]);

        var paths = Paths(index);
        Assert.Contains(_root, paths);
        Assert.Contains(Path.Combine(_root, "a.txt"), paths);
        Assert.Contains(Path.Combine(_root, "sub", "deep", "c.md"), paths);
        Assert.Contains(Path.Combine(_root, "empty"), paths);
        Assert.Equal(7, index.Count);
        var hit = Run(index, "b.bin").Hits.Single();
        Assert.Equal(300, hit.Size);
        Assert.NotNull(hit.Modified);
        Assert.True(Math.Abs((hit.Modified!.Value - DateTimeOffset.UtcNow).TotalMinutes) < 5);
        Assert.Equal(Path.Combine(_root, "sub"), hit.Folder);
    }

    [Fact]
    public void ExcludesPruneByNameMaskAndAbsolutePath()
    {
        Write("keep/a.txt", 1);
        Write("node_modules/pkg/x.js", 1);
        Write("proj/.git/config", 1);
        Write("proj/src/m.cs", 1);
        Write("home/.cache/blob", 1);
        Write("home/.Trash-1000/t", 1);
        Write("skipme/deep/z.txt", 1);
        Write("skipme2/z.txt", 1);

        var index = Build(["*/node_modules", "*/.git", "*/.cache", "*/.Trash-*", Path.Combine(_root, "skipme")]);

        var paths = Paths(index);
        Assert.Contains(Path.Combine(_root, "keep", "a.txt"), paths);
        Assert.Contains(Path.Combine(_root, "proj", "src", "m.cs"), paths);
        Assert.Contains(Path.Combine(_root, "skipme2", "z.txt"), paths);
        Assert.DoesNotContain(paths, static path => path.Contains("node_modules", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, static path => path.Contains(".git", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, static path => path.Contains(".cache", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, static path => path.Contains(".Trash-", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.StartsWith(Path.Combine(_root, "skipme") + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.DoesNotContain(Path.Combine(_root, "skipme"), paths);
        Assert.Contains(Path.Combine(_root, "skipme2"), paths);
    }

    [Fact]
    public void LinksAreListedAsFilesAndNeverFollowed()
    {
        Write("big/huge.bin", 100);
        Write("real/small.bin", 1);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "real", "to-big"), Path.Combine(_root, "big"));
            File.CreateSymbolicLink(Path.Combine(_root, "real", "to-small"), Path.Combine(_root, "real", "small.bin"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var index = Build([]);

        var paths = Paths(index);
        Assert.Contains(Path.Combine(_root, "real", "to-big"), paths);
        Assert.DoesNotContain(Path.Combine(_root, "real", "to-big", "huge.bin"), paths);
        var link = Run(index, "to-big").Hits.Single();
        Assert.False(link.IsFolder);
        Assert.Equal(0, link.Size);
        Assert.Equal(1, Run(index, "huge.bin").Total);
    }

    [Fact]
    public void AnUnreadableOrMissingRootIsSkippedAndTheOthersStay()
    {
        Write("ok/a.txt", 1);
        var options = new IndexOptions([Path.Combine(_root, "nowhere"), Path.Combine(_root, "ok")], []);

        var index = IndexScanner.Build(options, null, TestContext.Current.CancellationToken);

        Assert.Equal([Path.Combine(_root, "ok"), Path.Combine(_root, "ok", "a.txt")], Paths(index).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ANestedRootIsWalkedOnceAndAnExcludedRootStillCounts()
    {
        Write("outer/inner/a.txt", 1);
        Write("outer/b.txt", 1);
        var outer = Path.Combine(_root, "outer");
        var inner = Path.Combine(outer, "inner");

        var index = IndexScanner.Build(new IndexOptions([outer, inner, outer], [inner]), null, TestContext.Current.CancellationToken);

        var paths = Paths(index);
        Assert.Equal(paths.Count, paths.Distinct().Count());
        Assert.Contains(Path.Combine(inner, "a.txt"), paths);
        Assert.Equal(4, paths.Count);
    }

    [Fact]
    public void ProgressCountsEntriesAndCancellingStopsTheWalk()
    {
        for (var i = 0; i < 2_500; i++)
        {
            Write($"d{i % 50}/f{i}.txt", 1);
        }

        var seen = new List<long>();
        var index = IndexScanner.Build(new IndexOptions([_root], []), new Collector(seen), TestContext.Current.CancellationToken);

        Assert.Equal(index.Count, seen[^1]);
        Assert.True(seen.Count >= 3);
        Assert.Equal(seen.Order().ToArray(), seen.ToArray());

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => IndexScanner.Build(new IndexOptions([_root], []), null, cancel.Token));
    }

    [Fact]
    public void NoRootsMeansTheMountedVolumes()
    {
        var roots = IndexScanner.ResolveRoots(new IndexOptions([], []));

        Assert.NotEmpty(roots);
        Assert.All(roots, static root => Assert.True(Directory.Exists(root)));
    }

    private NameIndex Build(string[] excludes) => IndexScanner.Build(new IndexOptions([_root], excludes));

    private static List<string> Paths(NameIndex index) => [.. Enumerable.Range(0, index.Count).Select(index.PathOf)];

    private void Write(string relative, int length)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }

    private sealed class Collector(List<long> seen) : IProgress<long>
    {
        public void Report(long value) => seen.Add(value);
    }
}
