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
}
