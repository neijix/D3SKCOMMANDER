using Avalonia;
using Avalonia.Controls;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

/// <summary>Rectangles of the shell's controls in window coordinates, read after a layout pass.</summary>
internal static class LayoutGeometry
{
    /// <summary>The BesidePanels region is 180 px wide when it has widgets.</summary>
    public const double BesideWidth = 180;

    public readonly record struct Fit(double LeftWidth, double RightWidth);

    public static Fit AssertFits(MainWindow window, string what)
    {
        window.UpdateLayout();
        var screen = new Rect(window.ClientSize);
        var left = Box(window, "LeftSlot");
        var right = Box(window, "RightSlot");
        var area = screen.Width - (window.BesidePanels.Count > 0 ? BesideWidth : 0);
        var half = area / 2;
        AssertInside(screen, left, what + ": left panel");
        AssertInside(screen, right, what + ": right panel");
        Assert.True(left.Width > 0 && left.Height > 0, what + ": left panel is empty " + left);
        Assert.True(left.Right <= half + 0.5, what + ": left panel " + left + " leaves its half (" + half + ")");
        Assert.True(right.Left >= half - 0.5, what + ": right panel " + right + " leaves its half (" + half + ")");
        AssertBars(window, screen, left, what);
        AssertRegions(window, screen, right, what);
        return new Fit(left.Width, right.Width);
    }

    public static Rect Box(MainWindow window, string name)
    {
        var control = window.FindControl<Control>(name);
        Assert.NotNull(control);
        var origin = control.TranslatePoint(default, window);
        Assert.True(origin.HasValue, name + " is not attached to the window");
        return new Rect(origin.Value, control.Bounds.Size);
    }

    public static int EntryIndex(PanelSnapshot snap, string prefix)
    {
        for (var i = 0; i < snap.Entries.Count; i++)
        {
            if (snap.Entries[i].Name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException("Missing " + prefix);
    }

    private static void AssertBars(MainWindow window, Rect screen, Rect panel, string what)
    {
        var menu = Box(window, "MainMenu");
        Assert.True(menu.Height > 0, what + ": menu bar was not drawn");
        AssertInside(screen, menu, what + ": menu bar");
        Assert.True(menu.Bottom <= panel.Top + 0.5, what + ": menu bar " + menu + " overlaps the panels " + panel);
        AssertMenu(window, "Files", what);
        AssertMenu(window, "Help", what);
        var keys = Box(window, "FunctionKeyBar");
        AssertInside(screen, keys, what + ": function keys");
        Assert.True(keys.Height > 0, what + ": function keys were not drawn");
        Assert.True(keys.Top >= panel.Bottom - 0.5, what + ": function keys " + keys + " overlap the panels " + panel);
        Assert.StartsWith("F3 ", window.FunctionKeys[0].Content, StringComparison.Ordinal);
        Assert.StartsWith("F8 ", window.FunctionKeys[5].Content, StringComparison.Ordinal);
        Assert.StartsWith("Alt+F4 ", window.FunctionKeys[7].Content, StringComparison.Ordinal);
        var status = Box(window, "StatusLine");
        AssertInside(screen, status, what + ": status line");
        Assert.True(status.Height > 0, what + ": status line was not drawn");
        Assert.True(status.Top >= panel.Bottom - 0.5 && status.Bottom <= keys.Top + 0.5, what + ": status line " + status + " is misplaced");
    }

    private static void AssertRegions(MainWindow window, Rect screen, Rect rightPanel, string what)
    {
        var beside = window.FindControl<Control>("BesideHost");
        Assert.NotNull(beside);
        Assert.Equal(window.BesidePanels.Count > 0, beside.IsVisible);
        if (beside.IsVisible)
        {
            var box = Box(window, "BesideHost");
            AssertInside(screen, box, what + ": beside panels");
            Assert.Equal(BesideWidth, box.Width, 0.5);
            Assert.True(box.Left >= rightPanel.Right - 0.5, what + ": beside panels " + box + " overlap the right panel");
        }

        if (window.UnderPanels.Count > 0)
        {
            var under = Box(window, "UnderHost");
            Assert.True(window.FindControl<Control>("UnderHost")!.IsVisible, what + ": under panels are hidden");
            AssertInside(screen, under, what + ": under panels");
        }
    }

    private static void AssertMenu(MainWindow window, string header, string what)
    {
        var bar = window.FindControl<Menu>("MainMenu");
        Assert.NotNull(bar);
        foreach (var item in bar.Items.OfType<MenuItem>())
        {
            if (item.Header as string == header)
            {
                var origin = item.TranslatePoint(default, window);
                Assert.True(origin.HasValue, what + ": menu " + header + " is detached");
                AssertInside(new Rect(window.ClientSize), new Rect(origin.Value, item.Bounds.Size), what + ": menu " + header);
                return;
            }
        }

        Assert.Fail(what + ": menu " + header + " was not drawn");
    }

    private static void AssertInside(Rect outer, Rect inner, string what) =>
        Assert.True(
            inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5
            && inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5,
            what + " " + inner + " leaves " + outer);
}
