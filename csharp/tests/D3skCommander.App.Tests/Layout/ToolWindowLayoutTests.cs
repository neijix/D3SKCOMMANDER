using System.Numerics;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

public class ToolWindowLayoutTests
{
    [Fact]
    public async Task ToolWindowsOpenOnScreen()
    {
        using var session = await LayoutSession.Open(TestContext.Current.CancellationToken);
        var kind = ToolKind.Define("layout-stub", "tool.log.title", 200, 120);
        var marker = new NamedWidget("StubTool");
        session.Tools.Register(kind, () => marker);
        var tool = session.Tools.Open(kind);
        tool.Position = new ScreenPoint(16, 16);
        tool.Width = 200;
        tool.Height = 120;

        foreach (var (width, height) in new (float Width, float Height)[] { (640, 400), (1280, 800) })
        {
            ImGuiProbe.Frames(1, _ => session.Prepare(width, height, 1), () =>
            {
                session.Draw(tools: true);
                var own = LayoutGeometry.NoAutoMergeWindows();
                Assert.Single(own);
                var screen = new LayoutGeometry.Box(Vector2.Zero, new Vector2(width, height));
                Assert.True(screen.Contains(own[0], 2f), width + "x" + height + ": stub " + own[0] + " is off the display");
            });
        }

        Assert.Same(marker, tool.Content);
        Assert.Same(session.Window, tool.Owner);
        Assert.Contains(tool, session.Window.OwnedWindows);
        Assert.Null(tool.Parent);
    }
}
