using D3skCommander.Core.DiskUsage;
using Xunit;

namespace D3skCommander.Core.Tests.DiskUsage;

public class DiskUsageScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "d3sk-du-" + Guid.NewGuid().ToString("N"));

    public DiskUsageScannerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var path in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
        {
            TryUnlock(path);
        }

        TryUnlock(_root);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SizesAddUpAndCountFilesAndFolders()
    {
        Write("a.bin", 100);
        Write("sub/b.bin", 300);
        Write("sub/deep/c.bin", 50);
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var tree = DiskUsageScanner.Scan(_root, null, TestContext.Current.CancellationToken);

        Assert.Equal(450, tree.Bytes);
        Assert.Equal(3, tree.Files);
        Assert.Equal(3, tree.Folders);
        Assert.Equal(0, tree.Unreadable);
        Assert.Equal(["sub", "a.bin", "empty"], tree.Children.Select(child => child.Name).ToArray());
        var sub = tree.Children[0];
        Assert.Equal(350, sub.Bytes);
        Assert.Equal(sub.Children.Sum(child => child.Bytes), sub.Bytes);
        Assert.Same(tree, sub.Parent);
    }

    [Fact]
    public void KeepsTheBiggestFilesAndSumsTheRest()
    {
        for (var i = 1; i <= 10; i++)
        {
            Write($"f{i:00}.bin", i * 10);
        }

        var tree = DiskUsageScanner.Scan(_root, null, TestContext.Current.CancellationToken, 3);

        Assert.Equal(550, tree.Bytes);
        Assert.Equal(10, tree.Files);
        var files = tree.Children.Where(child => child.IsFile).ToList();
        Assert.Equal(["f10.bin", "f09.bin", "f08.bin"], files.Select(file => file.Name).ToArray());
        var rest = Assert.Single(tree.Children, child => child.Kind == SizeNodeKind.SmallFiles);
        Assert.Equal(7, rest.Files);
        Assert.Equal(280, rest.Bytes);
        Assert.Equal(_root, rest.Path);
    }

    [Fact]
    public void DoesNotFollowLinks()
    {
        Write("big/huge.bin", 100_000);
        Write("real/small.bin", 10);
        var link = Path.Combine(_root, "real", "to-big");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_root, "big"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var tree = DiskUsageScanner.Scan(Path.Combine(_root, "real"), null, TestContext.Current.CancellationToken);

        Assert.Equal(10, tree.Bytes);
        Assert.Equal(2, tree.Files);
        Assert.Equal(0, tree.Folders);
    }

    [Fact]
    public void CountsAnUnreadableFolder()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root" || Environment.GetEnvironmentVariable("USER") == "root")
        {
            return;
        }

        Write("ok/a.bin", 20);
        Write("locked/hidden.bin", 500);
        var locked = Path.Combine(_root, "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        var tree = DiskUsageScanner.Scan(_root, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, tree.Unreadable);
        Assert.Equal(20, tree.Bytes);
        var node = Assert.Single(tree.Children, child => child.Name == "locked");
        Assert.True(node.IsUnreadable);
        Assert.Equal(1, node.Unreadable);
        Assert.False(Assert.Single(tree.Children, child => child.Name == "ok").IsUnreadable);
    }

    [Fact]
    public void ReportsProgressAndTheFinalTotals()
    {
        for (var i = 0; i < 600; i++)
        {
            Write($"f{i}.bin", 1);
        }

        var seen = new List<ScanProgress>();
        DiskUsageScanner.Scan(_root, new Sync<ScanProgress>(seen.Add), TestContext.Current.CancellationToken);

        Assert.True(seen.Count >= 2);
        Assert.Equal(600, seen[^1].Files);
        Assert.Equal(600, seen[^1].Bytes);
    }

    [Fact]
    public void CancelStopsTheWalk()
    {
        Write("a.bin", 1);
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => DiskUsageScanner.Scan(_root, null, source.Token));
    }

    [Fact]
    public void AMissingFolderThrows()
    {
        Assert.Throws<DirectoryNotFoundException>(() => DiskUsageScanner.Scan(Path.Combine(_root, "nope"), null, TestContext.Current.CancellationToken));
    }

    private void Write(string relative, int length)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }

    private static void TryUnlock(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private sealed class Sync<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
