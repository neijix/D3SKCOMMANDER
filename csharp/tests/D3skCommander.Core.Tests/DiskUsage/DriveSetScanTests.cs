using D3skCommander.Core.DiskUsage;
using Xunit;

namespace D3skCommander.Core.Tests.DiskUsage;

public class DriveSetScanTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "d3sk-ds-" + Guid.NewGuid().ToString("N"));

    public DriveSetScanTests()
    {
        Directory.CreateDirectory(_base);
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    [Fact]
    public void OneChildPerDriveAndTotalsAddUp()
    {
        var a = Drive("a", ("x.bin", 100), ("sub/y.bin", 40));
        var b = Drive("b", ("z.bin", 7));

        var tree = DiskUsageScanner.ScanDrives("Computer", "computer:", [a, b], [], null, Ct);

        Assert.Equal("Computer", tree.Name);
        Assert.Equal("computer:", tree.Path);
        Assert.Equal(["a", "b"], tree.Children.Select(c => c.Name).Order().ToArray());
        Assert.Equal(147, tree.Bytes);
        Assert.Equal(3, tree.Files);
        Assert.Equal(DiskUsageScanner.Scan(a, null, Ct).Bytes, tree.Children.Single(c => c.Path == a).Bytes);
        Assert.Equal(DiskUsageScanner.Scan(a, null, Ct).Name, tree.Children.Single(c => c.Path == a).Name);
    }

    [Fact]
    public void NoDrivesGivesAnEmptyRoot()
    {
        var tree = DiskUsageScanner.ScanDrives("Computer", "computer:", [], [], null, Ct);

        Assert.Empty(tree.Children);
        Assert.Equal(0, tree.Bytes);
    }

    [Fact]
    public void AMountPointInsideADriveIsNeitherWalkedNorCounted()
    {
        var a = Drive("a", ("x.bin", 10), ("mnt/big.bin", 500), ("keep/k.bin", 5));
        var mount = Path.Combine(a, "mnt");

        var tree = DiskUsageScanner.ScanDrives("C", "computer:", [a], [mount, a + Path.DirectorySeparatorChar], null, Ct);

        Assert.Equal(15, tree.Bytes);
        var drive = Assert.Single(tree.Children);
        Assert.DoesNotContain(drive.Children, c => c.Name == "mnt");
        Assert.Equal(1, drive.Folders);
    }

    [Fact]
    public void AnotherDriveInsideADriveIsNotCountedTwice()
    {
        var a = Drive("a", ("x.bin", 10));
        var inner = Path.Combine(a, "inner");
        Directory.CreateDirectory(inner);
        File.WriteAllBytes(Path.Combine(inner, "i.bin"), new byte[30]);

        var tree = DiskUsageScanner.ScanDrives("C", "computer:", [a, inner], [a, inner], null, Ct);

        Assert.Equal(40, tree.Bytes);
        Assert.Equal(10, tree.Children.Single(c => c.Path == a).Bytes);
        Assert.Equal(30, tree.Children.Single(c => c.Path == inner).Bytes);
    }

    [Fact]
    public void AMissingDriveIsAnUnreadableChildAndTheOthersStillScan()
    {
        var a = Drive("a", ("x.bin", 10));
        var gone = Path.Combine(_base, "gone");

        var tree = DiskUsageScanner.ScanDrives("C", "computer:", [gone, a], [], null, Ct);

        var bad = tree.Children.Single(c => c.Path == gone);
        Assert.True(bad.IsUnreadable);
        Assert.Equal(0, bad.Bytes);
        Assert.Equal(10, tree.Bytes);
        Assert.Equal(1, tree.Unreadable);
    }

    [Fact]
    public void ProgressSpansDrives()
    {
        var a = Drive("a", ("x.bin", 10));
        var b = Drive("b", ("y.bin", 20), ("z.bin", 1));
        var last = new ScanProgress(0, 0, 0);
        var progress = new SyncProgress(p => last = p);

        DiskUsageScanner.ScanDrives("C", "computer:", [a, b], [], progress, Ct);

        Assert.Equal(3, last.Files);
        Assert.Equal(31, last.Bytes);
    }

    [Fact]
    public void CancelThrows()
    {
        var a = Drive("a", ("x.bin", 10));
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => DiskUsageScanner.ScanDrives("C", "computer:", [a], [], null, source.Token));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Drive(string name, params (string Path, int Size)[] files)
    {
        var root = Path.Combine(_base, name);
        Directory.CreateDirectory(root);
        foreach (var (path, size) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[size]);
        }

        return root;
    }

    private sealed class SyncProgress(Action<ScanProgress> onReport) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => onReport(value);
    }
}
