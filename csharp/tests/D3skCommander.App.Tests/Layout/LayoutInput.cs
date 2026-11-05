using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.App.Shell;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

/// <summary>Mouse input for one probe, sent to the headless main window as real pointer events.</summary>
internal static class LayoutInput
{
    public static async Task PressFile(
        LayoutSession session,
        string prefix,
        bool ctrl,
        double shift,
        int lingerMs,
        CancellationToken cancellationToken)
    {
        session.Resize(1280, 800, 1);
        session.Settle();
        var index = LayoutGeometry.EntryIndex(session.Read(PanelSide.Left), prefix);
        var at = LayoutGeometry.FilePoint(session.Window, PanelSide.Left, index);
        var modifiers = ctrl ? RawInputModifiers.Control : RawInputModifiers.None;
        session.Window.MouseMove(at, modifiers);
        session.Window.MouseDown(at, MouseButton.Left, modifiers);
        if (shift > 0)
        {
            session.Window.MouseMove(new Point(at.X + shift, at.Y + shift * 0.5), modifiers);
        }

        if (lingerMs > 0)
        {
            await Task.Delay(lingerMs, cancellationToken);
        }

        session.Window.MouseUp(at, MouseButton.Left, modifiers);
        session.Settle();
    }

    public static void ClickBrowse(LayoutSession session, PanelSide side)
    {
        session.Resize(1280, 800, 1);
        session.Settle();
        var button = LayoutGeometry.Pane(session.Window, side).FindControl<Button>("BrowseButton");
        Assert.NotNull(button);
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), session.Window);
        Assert.True(at.HasValue, "the browse button is detached");
        session.Window.MouseMove(at.Value);
        session.Window.MouseDown(at.Value, MouseButton.Left);
        session.Window.MouseUp(at.Value, MouseButton.Left);
        session.Settle();
    }
}
