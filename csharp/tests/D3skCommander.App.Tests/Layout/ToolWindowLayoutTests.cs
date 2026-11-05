using Avalonia;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

public class ToolWindowLayoutTests
{
    [AvaloniaFact]
    public async Task ToolWindowsOpenOnScreen()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        var kind = ToolKind.Define("layout-stub", "tool.log.title", 200, 120);
        var marker = new NamedWidget("StubTool");
        session.Tools.Register(kind, () => marker);
        var tool = session.Tools.Open(kind);
        tool.Position = new PixelPoint(16, 16);
        tool.Width = 200;
        tool.Height = 120;

        // The frame comes from the host; it must stay on the screen at both window sizes.
        var area = session.Window.Screen.WorkingArea;
        foreach (var (width, height) in new (double Width, double Height)[] { (640, 400), (1280, 800) })
        {
            session.Resize(width, height, 1);
            var frame = tool.Read();
            var what = width + "x" + height + ": stub " + frame + " is off the screen " + area;
            Assert.True(frame.X >= area.X && frame.Y >= area.Y, what);
            Assert.True(frame.X + frame.Width <= area.X + area.Width, what);
            Assert.True(frame.Y + frame.Height <= area.Y + area.Height, what);
        }

        Assert.Same(marker, tool.View);
        Assert.Same(session.Window, tool.Owner);
        Assert.Contains(tool, session.Window.OwnedWindows);
        Assert.Null(tool.Parent);
    }
}
