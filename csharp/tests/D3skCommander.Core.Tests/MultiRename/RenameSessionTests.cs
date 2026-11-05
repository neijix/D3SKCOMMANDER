using D3skCommander.Core.Jobs;
using D3skCommander.Core.MultiRename;
using Xunit;

namespace D3skCommander.Core.Tests.MultiRename;

/// <summary>
/// P22 session: insert, remap, start/undo as one job, and a preview error blocking Start.
/// Ports the four session tests of v0.1 <c>src/ui/rename_view.rs</c>.
/// </summary>
public class RenameSessionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public void InsertAtCursorAndSelection()
    {
        Assert.Equal(("a[N]bc", 4), RenameSession.InsertAt("abc", 1, 1, "[N]"));
        Assert.Equal(("X", 1), RenameSession.InsertAt("abc", 0, 3, "X"));
        Assert.Equal(("aX", 2), RenameSession.InsertAt("abc", 3, 1, "X"));
        Assert.Equal(("abcZ", 4), RenameSession.InsertAt("abc", 99, 99, "Z"));
        Assert.Equal(("é€-x", 3), RenameSession.InsertAt("é€x", 2, 2, "-"));
    }

    [Fact]
    public void RemapFollowsARenameAndItsUndo()
    {
        var a = "/d/a";
        var b = "/d/b";
        var c = "/d/c";
        var done = new[] { new RenamePair(a, b) };
        var files = new[] { a, c };
        var after = RenameSession.Remap(files, done, forward: true);
        Assert.Equal([b, c], after);
        Assert.Equal([a, c], RenameSession.Remap(after, done, forward: false));
    }

    [Fact]
    public async Task StartThenUndoRoundTrip()
    {
        using var dir = new RenameWorkDir();
        var files = new[] { dir.Touch("a.txt"), dir.Touch("b.txt") };
        var jobs = new JobService();
        var session = new RenameSession(files);
        session.Rule.NameMask = "n[C]";
        session.Refresh();
        Assert.False(session.HasError);
        var started = session.Start(jobs);
        Assert.Single(jobs.Jobs);
        var applied = await started.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, applied.Outcome);
        Assert.Empty(applied.Failed);
        Assert.True(File.Exists(dir.PathOf("n1.txt")));
        Assert.True(File.Exists(dir.PathOf("n2.txt")));
        Assert.All(session.Files, path => Assert.True(File.Exists(path)));
        var undone = session.Undo(jobs);
        var restored = await undone.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, restored.Outcome);
        Assert.Empty(restored.Failed);
        Assert.Equal(2, jobs.Jobs.Count);
        Assert.True(File.Exists(dir.PathOf("a.txt")));
        Assert.True(File.Exists(dir.PathOf("b.txt")));
        Assert.Empty(session.Done);
    }

    [Fact]
    public void PreviewErrorBlocksStart()
    {
        using var dir = new RenameWorkDir();
        var files = new[] { dir.Touch("a.txt"), dir.Touch("b.txt") };
        var session = new RenameSession(files);
        session.Rule.NameMask = "same";
        session.Refresh();
        Assert.True(session.HasError);
        Assert.False(session.CanStart);
    }
}
