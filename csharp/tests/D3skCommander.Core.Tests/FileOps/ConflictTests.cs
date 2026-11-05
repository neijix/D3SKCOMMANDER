using D3skCommander.Core.FileOps;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

/// <summary>
/// P11: overwrite, skip, rename the new file, overwrite all older, apply to all, and cancel.
/// Ports all 9 tests of <c>src/core/fileops/tests/conflicts.rs</c>.
/// </summary>
public sealed class ConflictTests
{
    [Fact]
    public async Task ConflictEventDescribesBothFiles()
    {
        using var bench = new FileOpsTests.Bench();
        var (src, dest) = ConflictSetup(bench, FileOpsTests.NewTime, FileOpsTests.OldTime);

        var outcome = await FileOpsTests.Go(bench, FileOpKind.Copy, [src], dest, choice: ConflictChoice.Skip);

        var question = Assert.Single(outcome.Operation.Conflicts());
        Assert.Equal((6L, 6L), (question.SourceSize, question.TargetSize));
        Assert.Equal(Path.GetFileName(src), Path.GetFileName(question.SourcePath));
        Assert.Equal(Path.GetFullPath(Path.Combine(dest, "a.txt")), question.TargetPath);
        Assert.NotNull(question.SourceModified);
        Assert.NotNull(question.TargetModified);
        Assert.True(question.SourceModified > question.TargetModified);
    }

    [Fact]
    public async Task ConflictOverwrite()
    {
        using var bench = new FileOpsTests.Bench();
        var (src, dest) = ConflictSetup(bench, FileOpsTests.OldTime, FileOpsTests.NewTime);

        var outcome = await FileOpsTests.Go(bench, FileOpKind.Copy, [src], dest, choice: ConflictChoice.Overwrite);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("source", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task ConflictSkip()
    {
        using var bench = new FileOpsTests.Bench();
        var (src, dest) = ConflictSetup(bench, FileOpsTests.NewTime, FileOpsTests.OldTime);
        FileOpProgress? last = null;

        var outcome = await FileOpsTests.Go(
            bench,
            FileOpKind.Copy,
            [src],
            dest,
            choice: ConflictChoice.Skip,
            onProgress: progress => last = progress);

        Assert.Equal((0, 0, false), outcome.Finished);
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.NotNull(last);
        Assert.Equal((1, 6L), (last.DoneFiles, last.DoneBytes));
    }

    [Fact]
    public async Task ConflictOverwriteAllAsksOnce()
    {
        using var bench = new FileOpsTests.Bench();
        var (a, b, dest) = TwoConflicts(bench, FileOpsTests.OldTime, FileOpsTests.NewTime);

        var outcome = await FileOpsTests.Go(bench, FileOpKind.Copy, [a, b], dest, choice: ConflictChoice.OverwriteAll);

        Assert.Single(outcome.Operation.Conflicts());
        Assert.Equal((2, 0, false), outcome.Finished);
        Assert.Equal("source", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("source", File.ReadAllText(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public async Task ConflictSkipAllAsksOnce()
    {
        using var bench = new FileOpsTests.Bench();
        var (a, b, dest) = TwoConflicts(bench, FileOpsTests.NewTime, FileOpsTests.OldTime);

        var outcome = await FileOpsTests.Go(bench, FileOpKind.Copy, [a, b], dest, choice: ConflictChoice.SkipAll);

        Assert.Single(outcome.Operation.Conflicts());
        Assert.Equal((0, 0, false), outcome.Finished);
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public async Task ConflictOverwriteAllOlderReplacesOnlyOlderTargets()
    {
        using var bench = new FileOpsTests.Bench();
        var dest = bench.PathOf("out");
        var a = bench.PathOf("src", "a.txt");
        var b = bench.PathOf("src", "b.txt");
        FileOpsTests.WriteText(a, "source");
        FileOpsTests.SetMtime(a, FileOpsTests.NewTime);
        FileOpsTests.WriteText(Path.Combine(dest, "a.txt"), "target");
        FileOpsTests.SetMtime(Path.Combine(dest, "a.txt"), FileOpsTests.OldTime);
        FileOpsTests.WriteText(b, "source");
        FileOpsTests.SetMtime(b, FileOpsTests.OldTime);
        FileOpsTests.WriteText(Path.Combine(dest, "b.txt"), "target");
        FileOpsTests.SetMtime(Path.Combine(dest, "b.txt"), FileOpsTests.NewTime);

        var outcome = await FileOpsTests.Go(
            bench,
            FileOpKind.Copy,
            [a, b],
            dest,
            choice: ConflictChoice.OverwriteAllOlder);

        Assert.Single(outcome.Operation.Conflicts());
        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("source", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public async Task ConflictRenameNewWritesAUniqueName()
    {
        using var bench = new FileOpsTests.Bench();
        var (src, dest) = ConflictSetup(bench, FileOpsTests.OldTime, FileOpsTests.NewTime);
        FileOpsTests.WriteText(Path.Combine(dest, "a (2).txt"), "taken");

        var outcome = await FileOpsTests.Go(bench, FileOpKind.Copy, [src], dest, choice: ConflictChoice.RenameNew);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("taken", File.ReadAllText(Path.Combine(dest, "a (2).txt")));
        Assert.Equal("source", File.ReadAllText(Path.Combine(dest, "a (3).txt")));
    }

    [Fact]
    public async Task ConflictCancelStopsTheOperation()
    {
        using var bench = new FileOpsTests.Bench();
        var (a, b, dest) = TwoConflicts(bench, FileOpsTests.OldTime, FileOpsTests.NewTime);

        var outcome = await FileOpsTests.Go(bench, FileOpKind.Copy, [a, b], dest, choice: ConflictChoice.Cancel);

        Assert.Equal((0, 0, true), outcome.Finished);
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "b.txt")));
        Assert.Single(outcome.Operation.Conflicts());
    }

    [Fact]
    public async Task ADroppedReplyCancels()
    {
        using var bench = new FileOpsTests.Bench();
        var (src, dest) = ConflictSetup(bench, FileOpsTests.OldTime, FileOpsTests.NewTime);

        var outcome = await FileOpsTests.Go(
            bench,
            FileOpKind.Copy,
            [src],
            dest,
            answer: (_, _) => Task.FromCanceled<ConflictChoice>(new CancellationToken(canceled: true)));

        Assert.Equal((0, 0, true), outcome.Finished);
        Assert.Equal("target", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    private static (string Source, string Dest) ConflictSetup(FileOpsTests.Bench bench, long srcTime, long dstTime)
    {
        var src = bench.PathOf("src", "a.txt");
        FileOpsTests.WriteText(src, "source");
        FileOpsTests.SetMtime(src, srcTime);
        var dest = bench.PathOf("out");
        var target = Path.Combine(dest, "a.txt");
        FileOpsTests.WriteText(target, "target");
        FileOpsTests.SetMtime(target, dstTime);
        return (src, dest);
    }

    private static (string A, string B, string Dest) TwoConflicts(FileOpsTests.Bench bench, long srcTime, long dstTime)
    {
        var dest = bench.PathOf("out");
        string[] sources = ["a.txt", "b.txt"];
        foreach (var name in sources)
        {
            var src = bench.PathOf("src", name);
            FileOpsTests.WriteText(src, "source");
            FileOpsTests.SetMtime(src, srcTime);
            var target = Path.Combine(dest, name);
            FileOpsTests.WriteText(target, "target");
            FileOpsTests.SetMtime(target, dstTime);
        }

        return (bench.PathOf("src", "a.txt"), bench.PathOf("src", "b.txt"), dest);
    }
}
