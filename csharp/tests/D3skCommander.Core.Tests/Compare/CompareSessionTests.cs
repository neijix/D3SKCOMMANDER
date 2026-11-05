using D3skCommander.Core.Compare;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.Compare;

/// <summary>P24: the four non-window tests of <c>src/ui/compare_view.rs</c>.</summary>
public class CompareSessionTests
{
    [Fact]
    public void ComparesInTheBackgroundAndReportsBlocks()
    {
        using var dir = new TempFolder();
        var left = Path.Combine(dir.Path, "a.txt");
        var right = Path.Combine(dir.Path, "b.txt");
        File.WriteAllBytes(left, "x\ny\nz\n"u8.ToArray());
        File.WriteAllBytes(right, "x\nY\nz\nw\n"u8.ToArray());

        var session = new CompareSession(left, right, new JobService());
        session.CompareFiles();
        Assert.Equal(CompareKind.None, session.Outcome.Kind);
        Assert.True(session.HasPendingResult);
        Wait(session);

        Assert.Equal(CompareKind.Text, session.Outcome.Kind);
        Assert.Equal((2, 4), (session.Outcome.Blocks, session.Outcome.Rows.Count));
        session.GoToDifference(true);
        Assert.Equal(1, session.Current);
        session.GoToDifference(true);
        Assert.Equal(3, session.Current);
        session.GoToDifference(false);
        Assert.Equal(1, session.Current);
    }

    [Fact]
    public void BinaryFilesReportTheFirstOffset()
    {
        using var dir = new TempFolder();
        var left = Path.Combine(dir.Path, "a.bin");
        var right = Path.Combine(dir.Path, "b.bin");
        File.WriteAllBytes(left, [0, 1, 2, 3]);
        File.WriteAllBytes(right, [0, 1, 9, 3]);

        var session = new CompareSession(left, right, new JobService());
        session.CompareFiles();
        Wait(session);

        Assert.Equal(CompareKind.Binary, session.Outcome.Kind);
        Assert.False(session.Outcome.Identical);
        Assert.Equal(2, session.Outcome.FirstDifference);
        var peek = session.Outcome.Peek;
        Assert.NotNull(peek);
        Assert.Equal(2, peek.Left[2]);
        Assert.Equal(9, peek.Right[2]);
    }

    [Fact]
    public void MissingFileIsAnInlineError()
    {
        using var dir = new TempFolder();
        var session = new CompareSession(
            Path.Combine(dir.Path, "nope"),
            Path.Combine(dir.Path, "nope2"),
            new JobService());
        session.CompareFiles();
        Wait(session);

        Assert.Equal(CompareKind.Error, session.Outcome.Kind);
        Assert.False(string.IsNullOrEmpty(session.Outcome.Error));
    }

    [Fact]
    public void EditBuffersRecompareAndSaveKeepsLineEndings()
    {
        using var dir = new TempFolder();
        var left = Path.Combine(dir.Path, "a.txt");
        var right = Path.Combine(dir.Path, "b.txt");
        File.WriteAllBytes(left, "one\r\ntwo\r\n"u8.ToArray());
        File.WriteAllBytes(right, "one\ntwo\n"u8.ToArray());

        var session = new CompareSession(left, right, new JobService());
        session.EnterEdit();
        Assert.Equal(CompareMode.Edit, session.Mode);
        session.SetText(0, "one\nTWO\n");
        session.RecompareBuffers();
        Wait(session);

        Assert.Equal(CompareKind.Text, session.Outcome.Kind);
        Assert.Equal(1, session.Outcome.Blocks);
        var reloaded = 0;
        session.ReloadRequested += () => reloaded++;
        session.Save(0);

        Assert.Equal("one\r\nTWO\r\n"u8.ToArray(), File.ReadAllBytes(left));
        Assert.Equal(1, reloaded);
    }

    private static void Wait(CompareSession session)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (session.HasPendingResult && DateTime.UtcNow < end)
        {
            session.Poll();
            Thread.Sleep(5);
        }

        session.Poll();
        Assert.False(session.HasPendingResult);
    }
}
