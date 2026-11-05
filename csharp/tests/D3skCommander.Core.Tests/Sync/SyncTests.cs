using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Sync;
using Xunit;

namespace D3skCommander.Core.Tests.Sync;

/// <summary>P25: the fifteen tests of <c>src/core/sync.rs</c>, plus the plan running as a job.</summary>
public class SyncTests
{
    [Fact]
    public void EveryStatusWithNestedDirs()
    {
        using var pair = new Pair();
        Put(pair.Left, "same.txt", "x", 10);
        Put(pair.Right, "same.txt", "x", 10);
        Put(pair.Left, "lonely.txt", "x", 10);
        Put(pair.Right, "orphan.txt", "x", 10);
        Put(pair.Left, "new_left.txt", "aa", 100);
        Put(pair.Right, "new_left.txt", "bb", 10);
        Put(pair.Left, "sub/new_right.txt", "aa", 10);
        Put(pair.Right, "sub/new_right.txt", "bb", 100);
        Put(pair.Left, "sub/deep/diff.txt", "short", 10);
        Put(pair.Right, "sub/deep/diff.txt", "longer text", 10);
        Put(pair.Left, "sub/deep/only.txt", "x", 10);

        var items = Run(pair.Left, pair.Right, new SyncOptions());
        var expect = new (string Rel, SyncStatus Status, SyncAction Action)[]
        {
            ("same.txt", SyncStatus.Equal, SyncAction.None),
            ("lonely.txt", SyncStatus.LeftOnly, SyncAction.CopyToRight),
            ("orphan.txt", SyncStatus.RightOnly, SyncAction.CopyToLeft),
            ("new_left.txt", SyncStatus.LeftNewer, SyncAction.CopyToRight),
            ("sub/new_right.txt", SyncStatus.RightNewer, SyncAction.CopyToLeft),
            ("sub/deep/diff.txt", SyncStatus.Different, SyncAction.None),
            ("sub/deep/only.txt", SyncStatus.LeftOnly, SyncAction.CopyToRight),
        };
        Assert.Equal(expect.Length, items.Count);
        foreach (var row in expect)
        {
            var item = Find(items, row.Rel);
            Assert.Equal(row.Status, item.Status);
            Assert.Equal(row.Action, item.Action);
            Assert.False(item.IsDir);
        }

        var newer = Find(items, "new_left.txt");
        Assert.Equal(2ul, newer.Left!.Size);
        Assert.Equal(At(10).ToUnixTimeSeconds(), Unix(newer.Right!.Modified));
        Assert.Null(Find(items, "lonely.txt").Right);
        for (var i = 1; i < items.Count; i++)
        {
            Assert.True(string.Compare(items[i - 1].Rel, items[i].Rel, StringComparison.Ordinal) <= 0);
        }
    }

    [Fact]
    public void TimeTolerance()
    {
        using var pair = new Pair();
        Put(pair.Left, "f", "x", 11);
        Put(pair.Right, "f", "x", 10);
        Assert.Equal(SyncStatus.Equal, Run(pair.Left, pair.Right, new SyncOptions())[0].Status);
        var strict = new SyncOptions { TimeToleranceSecs = 0 };
        Assert.Equal(SyncStatus.LeftNewer, Run(pair.Left, pair.Right, strict)[0].Status);
    }

    [Fact]
    public void NoSubdirs()
    {
        using var pair = new Pair();
        Put(pair.Left, "top.txt", "x", 10);
        Put(pair.Left, "sub/inner.txt", "x", 10);
        var items = Run(pair.Left, pair.Right, new SyncOptions { Subdirs = false });
        var item = Assert.Single(items);
        Assert.Equal("top.txt", item.Rel);
    }

    [Fact]
    public void MaskFiltersFiles()
    {
        using var pair = new Pair();
        Put(pair.Left, "a.txt", "x", 10);
        Put(pair.Left, "b.bin", "x", 10);
        Put(pair.Left, "sub/c.txt", "x", 10);
        Put(pair.Left, "sub/d.bak", "x", 10);
        var text = Run(pair.Left, pair.Right, new SyncOptions { Mask = "*.txt" });
        Assert.Equal(new[] { "a.txt", Slash("sub/c.txt") }, text.Select(item => item.Rel).ToArray());
        var kept = Run(pair.Left, pair.Right, new SyncOptions { Mask = "* | *.bak;*.bin" });
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void ByContent()
    {
        using var pair = new Pair();
        Put(pair.Left, "moved.txt", "same", 100);
        Put(pair.Right, "moved.txt", "same", 10);
        Put(pair.Left, "sneaky.txt", "aaaa", 10);
        Put(pair.Right, "sneaky.txt", "bbbb", 10);

        var plain = Run(pair.Left, pair.Right, new SyncOptions());
        Assert.Equal(SyncStatus.LeftNewer, Find(plain, "moved.txt").Status);
        Assert.Equal(SyncStatus.Equal, Find(plain, "sneaky.txt").Status);

        var items = Run(pair.Left, pair.Right, new SyncOptions { ByContent = true });
        Assert.Equal(SyncStatus.Equal, Find(items, "moved.txt").Status);
        Assert.Equal(SyncAction.None, Find(items, "moved.txt").Action);
        Assert.Equal(SyncStatus.Different, Find(items, "sneaky.txt").Status);
    }

    [Fact]
    public void IgnoreDate()
    {
        using var pair = new Pair();
        Put(pair.Left, "a", "same", 100);
        Put(pair.Right, "a", "same", 10);
        Put(pair.Left, "b", "one", 100);
        Put(pair.Right, "b", "three", 10);
        var items = Run(pair.Left, pair.Right, new SyncOptions { IgnoreDate = true });
        Assert.Equal(SyncStatus.Equal, Find(items, "a").Status);
        Assert.Equal(SyncStatus.Different, Find(items, "b").Status);
    }

    [Fact]
    public void EmptyDirs()
    {
        using var pair = new Pair();
        Directory.CreateDirectory(Path.Combine(pair.Left, Slash("hollow/inner")));
        Directory.CreateDirectory(Path.Combine(pair.Right, "rhollow"));
        Put(pair.Left, "full/f.txt", "x", 10);
        Directory.CreateDirectory(Path.Combine(pair.Left, "both"));
        Directory.CreateDirectory(Path.Combine(pair.Right, "both"));

        var items = Run(pair.Left, pair.Right, new SyncOptions());
        Assert.Equal(
            new[] { Slash("full/f.txt"), Slash("hollow/inner"), "rhollow" },
            items.Select(item => item.Rel).ToArray());
        var inner = Find(items, "hollow/inner");
        Assert.True(inner.IsDir);
        Assert.Equal(SyncStatus.LeftOnly, inner.Status);
        Assert.Equal(SyncAction.CopyToRight, inner.Action);
        Assert.Null(inner.Right);
        var right = Find(items, "rhollow");
        Assert.Equal(SyncStatus.RightOnly, right.Status);
        Assert.Equal(SyncAction.CopyToLeft, right.Action);

        var off = Run(pair.Left, pair.Right, new SyncOptions { EmptyDirs = false });
        Assert.Equal(new[] { Slash("full/f.txt") }, off.Select(item => item.Rel).ToArray());
    }

    [Fact]
    public void AsymmetricDefaultActions()
    {
        using var pair = new Pair();
        Put(pair.Left, "left_only", "x", 10);
        Put(pair.Right, "right_only", "x", 10);
        Put(pair.Left, "left_newer", "aa", 100);
        Put(pair.Right, "left_newer", "bb", 10);
        Put(pair.Left, "right_newer", "aa", 10);
        Put(pair.Right, "right_newer", "bb", 100);
        Put(pair.Left, "different", "a", 10);
        Put(pair.Right, "different", "bbb", 10);
        Put(pair.Left, "equal", "x", 10);
        Put(pair.Right, "equal", "x", 10);
        Directory.CreateDirectory(Path.Combine(pair.Right, "rdir"));

        var items = Run(pair.Left, pair.Right, new SyncOptions { Asymmetric = true });
        var expect = new (string Rel, SyncAction Action)[]
        {
            ("left_only", SyncAction.CopyToRight),
            ("right_only", SyncAction.DeleteRight),
            ("left_newer", SyncAction.CopyToRight),
            ("right_newer", SyncAction.CopyToRight),
            ("different", SyncAction.CopyToRight),
            ("equal", SyncAction.None),
            ("rdir", SyncAction.DeleteRight),
        };
        foreach (var row in expect)
        {
            Assert.Equal(row.Action, Find(items, row.Rel).Action);
        }
    }

    [Fact]
    public void CancelAndBadRoot()
    {
        using var pair = new Pair();
        Put(pair.Left, "a", "x", 10);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var cancelled = DirectorySync.Compare(pair.Left, pair.Right, new SyncOptions(), stop.Token);
        Assert.False(cancelled.IsSuccess);
        Assert.Equal(ErrorKind.Cancelled, cancelled.Error!.Kind);

        var missing = DirectorySync.Compare(Path.Combine(pair.Left, "nope"), pair.Right, new SyncOptions(), CancellationToken.None);
        Assert.False(missing.IsSuccess);
        var file = DirectorySync.Compare(Path.Combine(pair.Left, "a"), pair.Right, new SyncOptions(), CancellationToken.None);
        Assert.False(file.IsSuccess);
    }

    [Fact]
    public void FileFacingDirectoryListsTheFileOnly()
    {
        using var pair = new Pair();
        Put(pair.Left, "x", "file", 10);
        Put(pair.Right, "x/inner.txt", "y", 10);
        var item = Find(Run(pair.Left, pair.Right, new SyncOptions()), "x");
        Assert.False(item.IsDir);
        Assert.Equal(SyncStatus.LeftOnly, item.Status);
        Assert.Null(item.Right);
    }

    [Fact]
    public void ApplyCopiesBothWaysPreservingMtime()
    {
        using var pair = new Pair();
        Put(pair.Left, "to_right.txt", "from left", 500);
        Put(pair.Left, "sub/nested.txt", "nested", 600);
        Put(pair.Right, "to_left.txt", "from right", 700);
        Directory.CreateDirectory(Path.Combine(pair.Left, Slash("empty/deeper")));
        var items = Run(pair.Left, pair.Right, new SyncOptions());

        var seen = new List<string>();
        var report = DirectorySync.Apply(pair.Left, pair.Right, items, CancellationToken.None, rel =>
        {
            seen.Add(rel);
            return true;
        });
        Assert.Empty(report.Errors);
        Assert.Equal((4, 0), (report.Copied, report.Deleted));
        Assert.Equal(4, seen.Count);
        Assert.Equal("from left", File.ReadAllText(Path.Combine(pair.Right, "to_right.txt")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(pair.Right, Slash("sub/nested.txt"))));
        Assert.Equal("from right", File.ReadAllText(Path.Combine(pair.Left, "to_left.txt")));
        Assert.True(Directory.Exists(Path.Combine(pair.Right, Slash("empty/deeper"))));
        Assert.Equal(At(500).ToUnixTimeSeconds(), Mtime(Path.Combine(pair.Right, "to_right.txt")));
        Assert.Equal(At(600).ToUnixTimeSeconds(), Mtime(Path.Combine(pair.Right, Slash("sub/nested.txt"))));
        Assert.Equal(At(700).ToUnixTimeSeconds(), Mtime(Path.Combine(pair.Left, "to_left.txt")));

        var again = Run(pair.Left, pair.Right, new SyncOptions());
        Assert.All(again, item => Assert.Equal(SyncStatus.Equal, item.Status));
    }

    [Fact]
    public void ApplyOverwritesTheOlderFile()
    {
        using var pair = new Pair();
        Put(pair.Left, "f.txt", "old", 10);
        Put(pair.Right, "f.txt", "newer!", 100);
        var items = Run(pair.Left, pair.Right, new SyncOptions());
        Assert.Equal(SyncAction.CopyToLeft, items[0].Action);
        var report = DirectorySync.Apply(pair.Left, pair.Right, items, CancellationToken.None, _ => true);
        Assert.Equal(1, report.Copied);
        Assert.Equal("newer!", File.ReadAllText(Path.Combine(pair.Left, "f.txt")));
    }

    [Fact]
    public void ApplyDeletesInAsymmetricMode()
    {
        using var pair = new Pair();
        Put(pair.Left, "keep.txt", "k", 10);
        Put(pair.Right, "stale.txt", "s", 10);
        Put(pair.Right, "sub/stale2.txt", "s", 10);
        Directory.CreateDirectory(Path.Combine(pair.Right, "rempty"));
        var items = Run(pair.Left, pair.Right, new SyncOptions { Asymmetric = true });
        var report = DirectorySync.Apply(pair.Left, pair.Right, items, CancellationToken.None, _ => true);
        Assert.Empty(report.Errors);
        Assert.Equal((1, 3), (report.Copied, report.Deleted));
        Assert.True(File.Exists(Path.Combine(pair.Right, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(pair.Right, "stale.txt")));
        Assert.False(File.Exists(Path.Combine(pair.Right, Slash("sub/stale2.txt"))));
        Assert.False(Directory.Exists(Path.Combine(pair.Right, "rempty")));
        Assert.True(File.Exists(Path.Combine(pair.Left, "keep.txt")));
    }

    [Fact]
    public void ApplyHonoursEditedActionsCancelAndProgress()
    {
        using var pair = new Pair();
        Put(pair.Left, "a.txt", "a", 10);
        Put(pair.Left, "b.txt", "b", 10);
        var items = Run(pair.Left, pair.Right, new SyncOptions());
        items[0].Action = SyncAction.DeleteLeft;
        var report = DirectorySync.Apply(pair.Left, pair.Right, items, CancellationToken.None, _ => true);
        Assert.Equal((1, 1), (report.Copied, report.Deleted));
        Assert.False(File.Exists(Path.Combine(pair.Left, "a.txt")));
        Assert.False(File.Exists(Path.Combine(pair.Right, "a.txt")));
        Assert.True(File.Exists(Path.Combine(pair.Right, "b.txt")));

        using var stopped = new Pair();
        Put(stopped.Left, "a.txt", "a", 10);
        var once = Run(stopped.Left, stopped.Right, new SyncOptions());
        var skipped = DirectorySync.Apply(stopped.Left, stopped.Right, once, CancellationToken.None, _ => false);
        Assert.Equal(0, skipped.Copied);
        Assert.False(File.Exists(Path.Combine(stopped.Right, "a.txt")));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var halted = DirectorySync.Apply(stopped.Left, stopped.Right, once, cancel.Token, _ => true);
        Assert.Equal(0, halted.Copied);
    }

    [Fact]
    public void ApplyReportsErrorsAndRejectsUnsafePaths()
    {
        using var pair = new Pair();
        var side = new SyncSide(1, null);
        SyncItem Item(string rel, SyncAction action) =>
            new(rel, false, side, null, SyncStatus.LeftOnly, action);
        var items = new[]
        {
            Item(Slash("../escape.txt"), SyncAction.CopyToRight),
            Item("missing.txt", SyncAction.CopyToRight),
        };
        var report = DirectorySync.Apply(pair.Left, pair.Right, items, CancellationToken.None, _ => true);
        Assert.Equal((0, 0), (report.Copied, report.Deleted));
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains("unsafe", report.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyRunsAsAJob()
    {
        using var pair = new Pair();
        Put(pair.Left, "to_right.txt", "from left", 500);
        var items = Run(pair.Left, pair.Right, new SyncOptions());
        var operation = new SyncService(new JobService()).Apply(pair.Left, pair.Right, items);
        Assert.Equal(SyncService.ApplyTitle, operation.Job.Title);
        var result = await operation.Job.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.Equal(1, operation.Report.Copied);
        Assert.Empty(operation.Report.Errors);
        Assert.Equal("from left", File.ReadAllText(Path.Combine(pair.Right, "to_right.txt")));
    }

    private static IReadOnlyList<SyncItem> Run(string left, string right, SyncOptions options)
    {
        var result = DirectorySync.Compare(left, right, options, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static SyncItem Find(IReadOnlyList<SyncItem> items, string rel)
    {
        var wanted = Slash(rel);
        foreach (var item in items)
        {
            if (item.Rel == wanted)
            {
                return item;
            }
        }

        throw new InvalidOperationException(rel + " not listed: " + string.Join(", ", items.Select(item => item.Rel)));
    }

    private static void Put(string root, string rel, string content, long secs)
    {
        var path = Path.Combine(root, Slash(rel));
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, At(secs).UtcDateTime);
    }

    private static DateTimeOffset At(long secs) => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000 + secs);

    private static long Unix(DateTimeOffset? time) => time!.Value.ToUnixTimeSeconds();

    private static long Mtime(string path) =>
        new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToUnixTimeSeconds();

    private static string Slash(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);

    private sealed class Pair : IDisposable
    {
        public Pair()
        {
            Left = Directory.CreateTempSubdirectory("d3sk-sync-l-").FullName;
            Right = Directory.CreateTempSubdirectory("d3sk-sync-r-").FullName;
        }

        public string Left { get; }

        public string Right { get; }

        public void Dispose()
        {
            if (Directory.Exists(Left))
            {
                Directory.Delete(Left, recursive: true);
            }

            if (Directory.Exists(Right))
            {
                Directory.Delete(Right, recursive: true);
            }
        }
    }
}
