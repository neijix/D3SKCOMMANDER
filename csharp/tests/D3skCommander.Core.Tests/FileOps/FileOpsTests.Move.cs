using System.Runtime.InteropServices;
using D3skCommander.Core.FileOps;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public partial class FileOpsTests
{
    [Fact]
    public async Task MoveOnTheSameDeviceRenames()
    {
        using var bench = new Bench();
        var file = bench.PathOf("a.txt");
        WriteText(file, "moved");
        SetMtime(file, OldTime);
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "sub", "b.txt"), "bb");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Move, [file, tree], dest);

        Assert.Equal((2, 0, false), outcome.Finished);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(tree));
        Assert.Equal("moved", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal(OldTime, Mtime(Path.Combine(dest, "a.txt")));
        Assert.Equal("bb", File.ReadAllText(Path.Combine(dest, "tree", "sub", "b.txt")));
        var progress = outcome.Operation.Job.Progress;
        Assert.Equal((2, 2), (progress.ItemsTotal, progress.ItemsDone));
    }

    [Fact]
    public async Task MoveOntoAnExistingFolderMergesAndAsksPerFile()
    {
        using var bench = new Bench();
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "new.txt"), "new");
        WriteText(Path.Combine(tree, "same.txt"), "from source");
        var dest = bench.PathOf("out");
        WriteText(Path.Combine(dest, "tree", "same.txt"), "from target");
        WriteText(Path.Combine(dest, "tree", "other.txt"), "other");

        var outcome = await Go(bench, FileOpKind.Move, [tree], dest, choice: ConflictChoice.Skip);

        Assert.Single(outcome.Operation.Conflicts());
        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("from target", File.ReadAllText(Path.Combine(dest, "tree", "same.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "tree", "new.txt")));
        Assert.Equal("other", File.ReadAllText(Path.Combine(dest, "tree", "other.txt")));
        Assert.Equal("from source", File.ReadAllText(Path.Combine(tree, "same.txt")));
        Assert.False(File.Exists(Path.Combine(tree, "new.txt")));
    }

    [Fact]
    public async Task MoveOverwriteReplacesTheTarget()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "source");
        var dest = bench.PathOf("out");
        WriteText(Path.Combine(dest, "a.txt"), "target");

        var outcome = await Go(bench, FileOpKind.Move, [src], dest, choice: ConflictChoice.Overwrite);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("source", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.False(File.Exists(src));
    }

    [Fact]
    public async Task MoveAFolderIntoItsSubtreeIsRefused()
    {
        using var bench = new Bench();
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "sub", "a.txt"), "a");

        var outcome = await Go(bench, FileOpKind.Move, [tree], Path.Combine(tree, "sub"));

        Assert.Equal((0, 1, false), outcome.Finished);
        Assert.Equal("a", File.ReadAllText(Path.Combine(tree, "sub", "a.txt")));
    }

    [Fact]
    public async Task OverwritingAFileWithItselfInAnotherCaseIsRefusedWhereCaseIsIgnored()
    {
        using var bench = new Bench();
        var src = bench.PathOf(Path.Combine("Dir", "a.txt"));
        WriteText(src, "source");
        var sameFolderOtherCase = bench.PathOf("dir");

        var outcome = await Go(bench, FileOpKind.Copy, [src], sameFolderOtherCase, choice: ConflictChoice.Overwrite);

        Assert.Equal("source", File.ReadAllText(src));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Equal((0, 1, false), outcome.Finished);
        }
    }

    [Fact]
    public async Task MoveAcrossDevicesCopiesThenDeletes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "cross-device rename is a unix check");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var shm = "/dev/shm";
        if (!Directory.Exists(shm))
        {
            return;
        }

        using var local = new Bench();
        var other = Path.Combine(shm, "d3sk-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            if (SameDevice(local.Root, other))
            {
                return;
            }

            var tree = local.PathOf("tree");
            WriteText(Path.Combine(tree, "sub", "a.txt"), "across");
            SetMtime(Path.Combine(tree, "sub", "a.txt"), OldTime);

            var outcome = await Go(local, FileOpKind.Move, [tree], other);

            Assert.Equal((1, 0, false), outcome.Finished);
            Assert.Equal("across", File.ReadAllText(Path.Combine(other, "tree", "sub", "a.txt")));
            Assert.Equal(OldTime, Mtime(Path.Combine(other, "tree", "sub", "a.txt")));
            Assert.False(Directory.Exists(tree));
        }
        finally
        {
            if (Directory.Exists(other))
            {
                Directory.Delete(other, recursive: true);
            }
        }
    }

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int SysStat(string path, byte[] buffer);

    private static bool SameDevice(string left, string right)
    {
        var a = Device(left);
        var b = Device(right);
        return a is null || b is null || a.Value == b.Value;
    }

    private static ulong? Device(string path)
    {
        var buffer = new byte[256];
        if (SysStat(path, buffer) != 0)
        {
            return null;
        }

        return BitConverter.ToUInt64(buffer, 0);
    }
}
