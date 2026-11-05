using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using Xunit;

namespace D3skCommander.App.Tests.ToolWindows;

public class ToolWindowHostTests
{
    [AvaloniaFact]
    public void AToolOpensAsItsOwnTopLevelWindow()
    {
        using var app = ToolWindowApp.Open();
        var tool = app.Tools.Open(ToolKind.Search);

        Assert.NotSame(app.Window, tool);
        Assert.Same(app.Window, tool.Owner);
        Assert.Contains(tool, app.Window.OwnedWindows);
        Assert.True(tool.IsVisible);
        Assert.Null(tool.Parent);
        Assert.Equal(app.Localizer.Get("tool.search.title"), tool.Title);
        Assert.Equal("Search files", app.Localizer.Get("tool.search.title"));
        Assert.DoesNotContain(app.Window.OwnedWindows, window => ReferenceEquals(window, app.Window));
    }

    [AvaloniaFact]
    public void ARegisteredStubOpensAsItsOwnWindow()
    {
        using var app = ToolWindowApp.Open();
        var kind = ToolKind.Define("stub", "tool.log.title", 480, 320);
        var marker = new NamedWidget("StubTool");
        app.Tools.Register(kind, () => marker);

        var tool = app.Tools.Open(kind);

        Assert.Contains(tool, app.Window.OwnedWindows);
        Assert.Same(app.Window, tool.Owner);
        Assert.Same(marker, tool.View);
        Assert.Equal(app.Localizer.Get("tool.log.title"), tool.Title);
        Assert.Null(tool.Parent);
    }

    [AvaloniaFact]
    public void PositionAndSizeComeBackOnTheNextOpen()
    {
        using var app = ToolWindowApp.Open();
        var tool = app.Tools.Open(ToolKind.Search);
        tool.Position = new PixelPoint(120, 80);
        tool.Width = 640;
        tool.Height = 400;
        tool.Close();

        Assert.False(app.Tools.IsOpen(ToolKind.Search));
        var again = app.Tools.Open(ToolKind.Search);

        Assert.Equal(new PixelPoint(120, 80), again.Position);
        Assert.Equal(640, (int)Math.Round(again.Width));
        Assert.Equal(400, (int)Math.Round(again.Height));
        Assert.Contains(again, app.Window.OwnedWindows);

        var other = app.Tools.Open(ToolKind.Editor);
        Assert.NotEqual(new PixelPoint(120, 80), other.Position);
        Assert.Equal(ToolKind.Editor.DefaultWidth, (int)Math.Round(other.Width));
        Assert.Equal(ToolKind.Editor.DefaultHeight, (int)Math.Round(other.Height));
    }

    [AvaloniaFact]
    public void AnOffScreenFrameIsClampedOnTheNextOpen()
    {
        using var app = ToolWindowApp.Open();
        var screen = app.Window.Screen.WorkingArea;
        var tool = app.Tools.Open(ToolKind.Lister);
        var requestedWidth = screen.Width + 500;
        var requestedHeight = 220;
        tool.Width = requestedWidth;
        tool.Height = requestedHeight;
        tool.Position = new PixelPoint(screen.X - 80, screen.Y + screen.Height - 15);
        tool.Close();

        var clamped = app.Tools.Open(ToolKind.Lister);
        var width = (int)Math.Round(clamped.Width);
        var height = (int)Math.Round(clamped.Height);

        Assert.Equal(screen.Width, width);
        Assert.Equal(requestedHeight, height);
        Assert.Equal(screen.X, clamped.Position.X);
        Assert.Equal(screen.Y + screen.Height - requestedHeight, clamped.Position.Y);
        Assert.InRange(clamped.Position.X, screen.X, screen.X + screen.Width - width);
        Assert.InRange(clamped.Position.Y, screen.Y, screen.Y + screen.Height - height);
        Assert.Contains(clamped, app.Window.OwnedWindows);
    }

    [AvaloniaFact]
    public void EscapeClosesTheToolWindow()
    {
        using var app = ToolWindowApp.Open();
        var tool = app.Tools.Open(ToolKind.Log);
        Assert.Contains(tool, app.Window.OwnedWindows);

        tool.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(tool.IsVisible);
        Assert.False(app.Tools.IsOpen(ToolKind.Log));
        Assert.DoesNotContain(tool, app.Window.OwnedWindows);
    }

    [AvaloniaFact]
    public void OneWindowOpensPerKind()
    {
        using var app = ToolWindowApp.Open();
        var first = app.Tools.Open(ToolKind.Search);
        var second = app.Tools.Open(ToolKind.Search);
        var other = app.Tools.Open(ToolKind.Ftp);

        Assert.Same(first, second);
        Assert.NotSame(first, other);
        Assert.Equal(2, app.Window.OwnedWindows.Count);
    }

    [AvaloniaFact]
    public void AnUnregisteredKindShowsAnEmptyNamedToolSurface()
    {
        using var app = ToolWindowApp.Open();
        var tool = app.Tools.Open(ToolKind.Passwords);

        Assert.Equal("ToolSurface", tool.View.Name);
        var surface = Assert.IsType<Avalonia.Controls.Border>(tool.Content);
        Assert.Equal("ToolSurface", surface.Name);
    }

    [AvaloniaFact]
    public async Task AContentThatRefusesKeepsItsWindowOpenOnEscapeAndWhenItsOwnerCloses()
    {
        using var app = ToolWindowApp.Open();
        var kind = ToolKind.Define("guarded", "tool.log.title", 480, 320);
        var guard = new GuardWidget();
        app.Tools.Register(kind, () => guard);
        var tool = app.Tools.Open(kind);

        tool.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal(1, guard.Asked);
        Assert.True(tool.IsVisible);
        guard.Answer(false);
        await Task.Yield();
        Assert.True(tool.IsVisible);

        app.Window.Close();
        guard.Answer(false);
        await Task.Yield();
        Assert.True(tool.IsVisible);
        Assert.True(app.Window.IsVisible);
        Assert.Contains(tool, app.Window.OwnedWindows);

        _ = tool.TryCloseAsync();
        guard.Answer(true);
        await Task.Yield();
        Assert.False(tool.IsVisible);
    }
}

/// <summary>A tool body whose close answer the test gives, later.</summary>
internal sealed class GuardWidget : IShellWidget, ICloseGuard
{
    private TaskCompletionSource<bool> _answer = new();

    public int Asked { get; private set; }

    public string? Name => "Guard";

    public void Draw()
    {
    }

    public Task<bool> CanCloseAsync()
    {
        Asked++;
        _answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _answer.Task;
    }

    public void Answer(bool close) => _answer.TrySetResult(close);
}
