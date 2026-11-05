using Avalonia.Headless.XUnit;
using D3skCommander.App.Shell;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

public class LayoutTests
{
    [AvaloniaFact]
    public async Task MainWindowFitsAtEverySizeAndScale()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        Assert.Equal(41, session.Read(PanelSide.Left).Selectable);
        foreach (var scale in new[] { 1d, 1.5, 2d })
        {
            foreach (var (width, height) in LayoutSession.Sizes)
            {
                var what = width + "x" + height + " @" + scale + "x";
                session.Resize(width, height, scale);
                Assert.Equal(scale, session.Window.RenderScaling);
                Assert.Equal(width, session.Window.ClientSize.Width, 0.5);
                Assert.Equal(height, session.Window.ClientSize.Height, 0.5);
                LayoutGeometry.AssertFits(session.Window, what);
            }
        }
    }

    [AvaloniaFact]
    public async Task WithoutABesideWidgetTheRegionIsHiddenAndThePanelsTakeTheFullWidth()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken, beside: false);
        foreach (var scale in new[] { 1d, 1.5, 2d })
        {
            foreach (var (width, height) in LayoutSession.Sizes)
            {
                var what = "no beside widget, " + width + "x" + height + " @" + scale + "x";
                session.Resize(width, height, scale);
                LayoutGeometry.AssertFits(session.Window, what, beside: false);
            }
        }
    }

    [AvaloniaFact]
    public async Task PanelsShrinkBackAfterTheWindowGetsSmaller()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        var steps = new (double Width, double Height)[]
        {
            (1920, 1080),
            (1280, 800),
            (800, 600),
            (640, 400),
            (1600, 900),
        };
        var widths = new List<LayoutGeometry.Fit>();
        foreach (var (width, height) in steps)
        {
            session.Resize(width, height, 1);
            widths.Add(LayoutGeometry.AssertFits(session.Window, "resized to " + width + "x" + height));
        }

        Assert.True(widths[3].LeftWidth < widths[0].LeftWidth, "the left panel did not shrink");
        Assert.True(widths[3].RightWidth < widths[0].RightWidth, "the right panel did not shrink");
        Assert.True(widths[4].LeftWidth > widths[3].LeftWidth, "the left panel did not grow again");
        Assert.True(widths[4].RightWidth > widths[3].RightWidth, "the right panel did not grow again");
    }

    [AvaloniaFact]
    public async Task AChosenFolderOpensInThePanelWhoseButtonWasClicked()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        var picked = Path.Combine(session.Directory, "picked folder");
        Directory.CreateDirectory(picked);
        File.WriteAllText(Path.Combine(picked, "inside the picked folder.txt"), "x");
        session.Picker.Enqueue(picked);

        LayoutInput.ClickBrowse(session, PanelSide.Right);
        await session.WaitUntil(
            () => session.Read(PanelSide.Right).Path == picked,
            TestContext.Current.CancellationToken);
        session.Settle();

        Assert.Equal(1, session.Picker.Calls);
        Assert.True(session.Lists(PanelSide.Right, "inside the picked folder"));
        Assert.False(session.Lists(PanelSide.Left, "inside the picked folder"));
        var index = LayoutGeometry.EntryIndex(session.Read(PanelSide.Right), "inside the picked folder");
        var at = LayoutGeometry.FilePoint(session.Window, PanelSide.Right, index);
        Assert.True(at.X > session.Window.ClientSize.Width / 2, "the chosen file is not in the right half");
    }

    [AvaloniaFact]
    public async Task APressOnAFileNameSelectsItEvenIfTheMouseMovesOrLingers()
    {
        foreach (var (shift, linger) in new (double Shift, int Linger)[] { (0, 0), (12, 0), (0, 1200) })
        {
            using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
            await LayoutInput.PressFile(session, "file number 5 with", ctrl: true, shift, linger, TestContext.Current.CancellationToken);
            Assert.Equal("1 / 41 selected", session.Status(PanelSide.Left));
            Assert.True(session.Marks(PanelSide.Left, "file number 5 with"), "move " + shift + " linger " + linger);
        }
    }

    [AvaloniaFact]
    public async Task ACtrlPressUnselectsAndAPlainPressClearsTheSelection()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        var token = TestContext.Current.CancellationToken;
        await LayoutInput.PressFile(session, "file number 5 with", ctrl: true, 0, 0, token);
        await LayoutInput.PressFile(session, "file number 6 with", ctrl: true, 0, 0, token);
        Assert.Equal("2 / 41 selected", session.Status(PanelSide.Left));
        Assert.True(session.Marks(PanelSide.Left, "file number 5 with"));
        Assert.True(session.Marks(PanelSide.Left, "file number 6 with"));

        await LayoutInput.PressFile(session, "file number 5 with", ctrl: true, 0, 0, token);
        Assert.Equal("1 / 41 selected", session.Status(PanelSide.Left));
        Assert.False(session.Marks(PanelSide.Left, "file number 5 with"));
        Assert.True(session.Marks(PanelSide.Left, "file number 6 with"));

        await LayoutInput.PressFile(session, "file number 5 with", ctrl: false, 0, 0, token);
        Assert.Equal("0 / 41 selected", session.Status(PanelSide.Left));
        Assert.False(session.Marks(PanelSide.Left, "file number 5 with"));
        Assert.False(session.Marks(PanelSide.Left, "file number 6 with"));
    }
}
