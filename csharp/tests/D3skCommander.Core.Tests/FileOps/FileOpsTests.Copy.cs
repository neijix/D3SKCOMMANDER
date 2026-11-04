using D3skCommander.Core.FileOps;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public partial class FileOpsTests
{
    [Fact]
    public async Task CopyFileKeepsContentAndMtime()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "hello");
        SetMtime(src, OldTime);
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal(OldTime, Mtime(Path.Combine(dest, "a.txt")));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public async Task CopyFileKeepsPermissions()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "unix file modes are not asserted on Windows");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var bench = new Bench();
        var src = bench.PathOf("run.sh");
        WriteText(src, "#!/bin/sh\n");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(src, mode);
        var dest = bench.PathOf("out");

        await Go(bench, FileOpKind.Copy, [src], dest);

        var copied = (int)File.GetUnixFileMode(Path.Combine(dest, "run.sh")) & 0x1FF;
        Assert.Equal((int)mode, copied);
    }

    [Fact]
    public async Task CopyTreeWithEmptyDirsAndTotals()
    {
        using var bench = new Bench();
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "top.txt"), "12345");
        WriteText(Path.Combine(tree, "sub", "deep", "leaf.txt"), "abc");
        Directory.CreateDirectory(Path.Combine(tree, "empty"));
        SetMtime(Path.Combine(tree, "sub", "deep", "leaf.txt"), OldTime);
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [tree], dest);

        Assert.Equal((2, 0, false), outcome.Finished);
        Assert.Equal("12345", File.ReadAllText(Path.Combine(dest, "tree", "top.txt")));
        Assert.Equal("abc", File.ReadAllText(Path.Combine(dest, "tree", "sub", "deep", "leaf.txt")));
        Assert.True(Directory.Exists(Path.Combine(dest, "tree", "empty")));
        Assert.Equal(OldTime, Mtime(Path.Combine(dest, "tree", "sub", "deep", "leaf.txt")));
        var progress = outcome.Operation.Job.Progress;
        Assert.Equal((2, 2), (progress.ItemsTotal, progress.ItemsDone));
        Assert.Equal((8L, 8L), (progress.BytesTotal, progress.BytesDone));
    }

    [Fact]
    public async Task CopyALargeFileInSeveralChunks()
    {
        using var bench = new Bench();
        var src = bench.PathOf("big.bin");
        var data = new byte[(2 * 1024 * 1024) + 123];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        File.WriteAllBytes(src, data);
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dest, "big.bin")));
        Assert.Equal(data.Length, outcome.Operation.Job.Progress.BytesDone);
    }

    [Fact]
    public async Task CopyWithRenameTo()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest, "b.txt");

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "b.txt")));
        Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task RenameToWithASeparatorIsRefused()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest, "../evil.txt");

        Assert.Equal((0, 1, false), outcome.Finished);
        Assert.False(File.Exists(bench.PathOf("evil.txt")));
    }

    [Fact]
    public async Task CopyWithoutADestinationIsAnError()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");

        var outcome = await Go(bench, FileOpKind.Copy, [src], null);

        Assert.Equal((0, 1, false), outcome.Finished);
        Assert.Contains("destination", outcome.Operation.ErrorMessages()[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSourceReportsAnErrorAndGoesOn()
    {
        using var bench = new Bench();
        var good = bench.PathOf("good.txt");
        WriteText(good, "ok");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [bench.PathOf("nope.txt"), good], dest);

        Assert.Equal((1, 1, false), outcome.Finished);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(dest, "good.txt")));
    }

    [Fact]
    public async Task CopyAFolderIntoItselfIsRefused()
    {
        using var bench = new Bench();
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));

        foreach (var dest in new[] { tree, Path.Combine(tree, "sub") })
        {
            var outcome = await Go(bench, FileOpKind.Copy, [tree], dest);
            Assert.Equal((0, 1, false), outcome.Finished);
            Assert.Contains("into itself", outcome.Operation.ErrorMessages()[0], StringComparison.Ordinal);
        }

        Assert.False(Directory.Exists(Path.Combine(tree, "tree")));
        Assert.False(Directory.Exists(Path.Combine(tree, "sub", "tree")));
    }

    [Fact]
    public async Task CopyOntoTheSamePathIsAnError()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "keep me");

        var outcome = await Go(bench, FileOpKind.Copy, [src], bench.Root);

        Assert.Equal((0, 1, false), outcome.Finished);
        Assert.Contains("same", outcome.Operation.ErrorMessages()[0], StringComparison.Ordinal);
        Assert.Equal("keep me", File.ReadAllText(src));
    }
}
