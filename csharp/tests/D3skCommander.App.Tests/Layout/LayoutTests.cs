using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

public class LayoutTests
{
    [Fact]
    public async Task MainWindowFitsAtEverySizeAndScale()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        Assert.Equal(41, session.Read(PanelSide.Left).Selectable);
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            foreach (var (width, height) in LayoutSession.Sizes)
            {
                var what = width + "x" + height + " @" + scale + "x";
                ImGuiProbe.Frames(1, _ => session.Prepare(width, height, scale), () =>
                {
                    session.Draw(false);
                    LayoutGeometry.AssertFits(session.Window, scale, what);
                });
            }
        }
    }

    [Fact]
    public async Task PanelsShrinkBackAfterTheWindowGetsSmaller()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        var steps = new (float Width, float Height)[]
        {
            (1920, 1080),
            (1280, 800),
            (800, 600),
            (640, 400),
            (1600, 900),
        };
        var widths = new List<(float Left, float Right)>();
        ImGuiProbe.Frames(steps.Length, frame => session.Prepare(steps[frame].Width, steps[frame].Height, 1), () =>
        {
            session.Draw(false);
            var fit = LayoutGeometry.AssertFits(session.Window, 1, "resized");
            widths.Add((fit.LeftWidth, fit.RightWidth));
        });

        Assert.True(widths[3].Left < widths[0].Left, "the left panel did not shrink");
        Assert.True(widths[3].Right < widths[0].Right, "the right panel did not shrink");
        Assert.True(widths[4].Left > widths[3].Left, "the left panel did not grow again");
        Assert.True(widths[4].Right > widths[3].Right, "the right panel did not grow again");
    }

    [Fact]
    public async Task AChosenFolderOpensInThePanelWhoseButtonWasClicked()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        var picked = Path.Combine(session.Directory, "picked folder");
        Directory.CreateDirectory(picked);
        File.WriteAllText(Path.Combine(picked, "inside the picked folder.txt"), "x");
        session.Picker.Enqueue(picked);

        LayoutInput.ClickBrowse(session, right: true);
        await session.WaitUntil(
            () => session.Read(PanelSide.Right).Path == picked,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, session.Picker.Calls);
        Assert.True(session.Lists(PanelSide.Right, "inside the picked folder"));
        Assert.False(session.Lists(PanelSide.Left, "inside the picked folder"));
        ImGuiProbe.Frames(1, _ => session.Prepare(1280, 800, 1), () =>
        {
            session.Draw(false);
            var index = LayoutGeometry.EntryIndex(session.Read(PanelSide.Right), "inside the picked folder");
            Assert.True(LayoutGeometry.TryRowInRightHalf(index), "the chosen file is not in the right half");
        });
    }

    [Fact]
    public async Task APressOnAFileNameSelectsItEvenIfTheMouseMovesOrLingers()
    {
        foreach (var (shift, frames) in new (float Shift, int Frames)[] { (0, 1), (12, 1), (0, 70) })
        {
            using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
            LayoutInput.PressFile(session, "file number 5 with", ctrl: true, shift, frames);
            Assert.Equal("1 / 41 selected", session.Status(PanelSide.Left));
            var hit = session.Read(PanelSide.Left);
            var cursor = hit.Entries[hit.Cursor].Name;
            Assert.True(session.Marks(PanelSide.Left, "file number 5 with"), "move " + shift + " hit " + cursor);
        }
    }

    [Fact]
    public async Task ACtrlPressUnselectsAndAPlainPressClearsTheSelection()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        LayoutInput.PressFile(session, "file number 5 with", ctrl: true, 0, 1);
        LayoutInput.PressFile(session, "file number 6 with", ctrl: true, 0, 1);
        Assert.Equal("2 / 41 selected", session.Status(PanelSide.Left));
        Assert.True(session.Marks(PanelSide.Left, "file number 5 with"));
        Assert.True(session.Marks(PanelSide.Left, "file number 6 with"));

        LayoutInput.PressFile(session, "file number 5 with", ctrl: true, 0, 1);
        Assert.Equal("1 / 41 selected", session.Status(PanelSide.Left));
        Assert.False(session.Marks(PanelSide.Left, "file number 5 with"));
        Assert.True(session.Marks(PanelSide.Left, "file number 6 with"));

        LayoutInput.PressFile(session, "file number 5 with", ctrl: false, 0, 1);
        Assert.Equal("0 / 41 selected", session.Status(PanelSide.Left));
        Assert.False(session.Marks(PanelSide.Left, "file number 5 with"));
        Assert.False(session.Marks(PanelSide.Left, "file number 6 with"));
    }
}
