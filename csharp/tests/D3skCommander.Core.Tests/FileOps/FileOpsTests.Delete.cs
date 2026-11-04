using D3skCommander.Core.Common;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public partial class FileOpsTests
{
    [Fact]
    public async Task CancelRemovesThePartialFile()
    {
        using var bench = new Bench();
        var src = bench.PathOf("big.bin");
        File.WriteAllBytes(src, new byte[3 * 1024 * 1024]);
        var small = bench.PathOf("z.txt");
        WriteText(small, "never copied");
        var dest = bench.PathOf("out");
        using var cancel = new CancellationTokenSource();

        var outcome = await Go(
            bench,
            FileOpKind.Copy,
            [src, small],
            dest,
            cancel: cancel,
            onProgress: progress =>
            {
                if (progress.CurrentPath == src)
                {
                    cancel.Cancel();
                }
            });

        Assert.Equal((0, 0, true), outcome.Finished);
        Assert.False(File.Exists(Path.Combine(dest, "big.bin")));
        Assert.False(File.Exists(Path.Combine(dest, "z.txt")));
    }

    [Fact]
    public async Task ARaisedFlagBeforeTheStartDoesNothing()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest, cancel: cancel);

        Assert.Equal((0, 0, true), outcome.Finished);
        Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task DeletePermanentlyRemovesFilesAndTrees()
    {
        using var bench = new Bench();
        var file = bench.PathOf("a.txt");
        WriteText(file, "x");
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "sub", "b.txt"), "bb");
        WriteText(Path.Combine(tree, "c.txt"), "c");

        var outcome = await Go(bench, FileOpKind.Delete, [file, tree], null);

        Assert.Equal((3, 0, false), outcome.Finished);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(tree));
        var progress = outcome.Operation.Job.Progress;
        Assert.Equal((3, 3), (progress.ItemsTotal, progress.ItemsDone));
    }

    [Fact]
    public async Task DeleteOfAMissingPathIsAnError()
    {
        using var bench = new Bench();

        var outcome = await Go(bench, FileOpKind.Delete, [bench.PathOf("ghost")], null);

        Assert.Equal((0, 1, false), outcome.Finished);
    }

    [Fact]
    public async Task DeleteNeverFollowsADirectorySymlink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "directory symlinks are a unix check");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var bench = new Bench();
        var real = bench.PathOf("real");
        WriteText(Path.Combine(real, "keep.txt"), "keep");
        var tree = bench.PathOf("tree");
        Directory.CreateDirectory(tree);
        File.CreateSymbolicLink(Path.Combine(tree, "link"), real);
        var top = bench.PathOf("toplink");
        File.CreateSymbolicLink(top, real);

        var outcome = await Go(bench, FileOpKind.Delete, [tree, top], null);

        Assert.Equal(0, outcome.Finished.Errors);
        Assert.False(Directory.Exists(tree));
        Assert.Null(LinkTargetOrNull(top));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(real, "keep.txt")));
    }

    [Fact]
    public async Task CopyKeepsSymlinksAsLinks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symlink copies are a unix check");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var bench = new Bench();
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "real.txt"), "r");
        File.CreateSymbolicLink(Path.Combine(tree, "link"), "real.txt");
        File.CreateSymbolicLink(Path.Combine(tree, "dangling"), "missing");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [tree], dest);

        Assert.Equal((3, 0, false), outcome.Finished);
        var link = Path.Combine(dest, "tree", "link");
        Assert.Equal("real.txt", LinkTargetOrNull(link));
        Assert.NotNull(LinkTargetOrNull(Path.Combine(dest, "tree", "dangling")));
    }

    [Fact]
    public async Task SpawnStreamsEventsUntilFinished()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");
        var seen = 0;

        var outcome = await Go(
            bench,
            FileOpKind.Copy,
            [src],
            dest,
            onProgress: _ => seen++);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.True(seen > 0);
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task SpawnConflictIsAnsweredOverTheChannel()
    {
        using var bench = new Bench();
        var src = bench.PathOf("src", "a.txt");
        WriteText(src, "source");
        SetMtime(src, OldTime);
        var dest = bench.PathOf("out");
        WriteText(Path.Combine(dest, "a.txt"), "target");
        SetMtime(Path.Combine(dest, "a.txt"), NewTime);

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest, choice: ConflictChoice.Overwrite);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("source", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task LogFileGetsOneLinePerFinishedFile()
    {
        using var bench = new Bench();
        bench.Log.Enabled = true;
        var src = bench.PathOf("logged.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");

        await Go(bench, FileOpKind.Copy, [src], dest);
        await Go(bench, FileOpKind.Copy, [bench.PathOf("ghost.txt")], dest);

        var lines = bench.Log.ReadLines();
        var ok = FindLine(lines, "logged.txt");
        var fields = ok.Split('\t');
        Assert.Equal(5, fields.Length);
        Assert.Equal("COPY", fields[1]);
        Assert.Equal(src, fields[2]);
        Assert.EndsWith("logged.txt", fields[3]);
        Assert.Equal("OK", fields[4]);
        Assert.Equal("2026-10-31 10:00:00".Length, fields[0].Length);
        var failed = FindLine(lines, "ghost.txt").Split('\t');
        Assert.StartsWith("ERROR ", failed[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedItemCanBeRetriedAlone()
    {
        using var bench = new Bench();
        var missing = bench.PathOf("ghost.txt");
        var good = bench.PathOf("good.txt");
        WriteText(good, "ok");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [missing, good], dest);

        Assert.Equal(JobOutcome.Completed, outcome.Result.Outcome);
        var failed = Assert.Single(outcome.Result.Failed);
        Assert.Equal(missing, failed.Item);
        Assert.Equal(ErrorKind.NotFound, failed.Error.Kind);
        Assert.True(failed.Retryable);
        WriteText(missing, "later");

        var retry = bench.Jobs.Retry(outcome.Operation.Job, [missing]);
        var again = await retry.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(JobOutcome.Completed, again.Outcome);
        Assert.Empty(again.Failed);
        Assert.Equal("later", File.ReadAllText(Path.Combine(dest, "ghost.txt")));
        Assert.Equal("ok", File.ReadAllText(Path.Combine(dest, "good.txt")));
    }

    private static string? LinkTargetOrNull(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string FindLine(IReadOnlyList<string> lines, string fragment)
    {
        foreach (var line in lines)
        {
            if (line.Contains(fragment, StringComparison.Ordinal))
            {
                return line;
            }
        }

        throw new InvalidOperationException("No log line contains " + fragment);
    }
}
