using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public partial class FileOpsTests
{
    [Fact]
    public void UniqueNameNumbersFromTwo()
    {
        using var bench = new Bench();
        var dir = bench.Root;
        Assert.Equal(Path.Combine(dir, "a.txt"), FileOpNames.UniqueName(dir, "a.txt"));
        WriteText(Path.Combine(dir, "a.txt"), "");
        Assert.Equal(Path.Combine(dir, "a (2).txt"), FileOpNames.UniqueName(dir, "a.txt"));
        WriteText(Path.Combine(dir, "a (2).txt"), "");
        Assert.Equal(Path.Combine(dir, "a (3).txt"), FileOpNames.UniqueName(dir, "a.txt"));
    }

    [Fact]
    public void UniqueNameHandlesNoExtensionAndDotFiles()
    {
        using var bench = new Bench();
        var dir = bench.Root;
        WriteText(Path.Combine(dir, "README"), "");
        WriteText(Path.Combine(dir, ".bashrc"), "");
        WriteText(Path.Combine(dir, "a.tar.gz"), "");
        Assert.Equal(Path.Combine(dir, "README (2)"), FileOpNames.UniqueName(dir, "README"));
        Assert.Equal(Path.Combine(dir, ".bashrc (2)"), FileOpNames.UniqueName(dir, ".bashrc"));
        Assert.Equal(Path.Combine(dir, "a.tar (2).gz"), FileOpNames.UniqueName(dir, "a.tar.gz"));
    }

    [Fact]
    public async Task TargetFolderVersusNewNameMatchesV01()
    {
        var folder = FileOpNames.FromDialog("/tmp/out", 1, targetIsDirectory: true);
        Assert.Equal("/tmp/out", folder.Directory);
        Assert.Null(folder.RenameTo);

        var trailed = FileOpNames.FromDialog("/tmp/out/", 1, targetIsDirectory: false);
        Assert.Equal("/tmp/out", trailed.Directory);
        Assert.Null(trailed.RenameTo);

        var named = FileOpNames.FromDialog("/tmp/out/b.txt", 1, targetIsDirectory: false);
        Assert.Equal(Path.Combine("/tmp/out", "b.txt").Replace("b.txt", "").TrimEnd(Path.DirectorySeparatorChar), named.Directory);
        Assert.Equal("/tmp/out", named.Directory);
        Assert.Equal("b.txt", named.RenameTo);

        var many = FileOpNames.FromDialog("/tmp/out/b.txt", 2, targetIsDirectory: false);
        Assert.Equal("/tmp/out/b.txt", many.Directory);
        Assert.Null(many.RenameTo);

        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");
        Directory.CreateDirectory(dest);
        var operation = bench.Ops.TransferToTarget(
            FileOpKind.Copy,
            bench.Provider,
            [src],
            bench.Provider,
            Path.Combine(dest, "b.txt"),
            targetIsDirectory: false,
            cancellationToken: TestContext.Current.CancellationToken);
        var result = await operation.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "b.txt")));
        Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task NewFolderCreatesTheFolder()
    {
        using var bench = new Bench();
        var created = bench.Ops.NewFolder(
            bench.Provider,
            bench.Root,
            Path.Combine("sub", "leaf"),
            TestContext.Current.CancellationToken);
        var result = await created.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.True(Directory.Exists(bench.PathOf("sub", "leaf")));

        var again = bench.Ops.NewFolder(
            bench.Provider,
            bench.Root,
            Path.Combine("sub", "leaf"),
            TestContext.Current.CancellationToken);
        var second = await again.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, second.Outcome);
        Assert.Empty(second.Failed);

        var empty = bench.Ops.NewFolder(bench.Provider, bench.Root, "", TestContext.Current.CancellationToken);
        var skipped = await empty.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, skipped.Outcome);
        Assert.Empty(skipped.Failed);
    }
}
