using System.Numerics;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using Hexa.NET.ImGui;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

/// <summary>Rectangles read back from the ImGui context after a frame. No OS window position.</summary>
internal static class LayoutGeometry
{
    public readonly record struct Box(Vector2 Min, Vector2 Max)
    {
        public float Width => Max.X - Min.X;

        public float Height => Max.Y - Min.Y;

        public Vector2 Center => new((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f);

        public bool Contains(Box inner, float slack) =>
            inner.Min.X >= Min.X - slack
            && inner.Min.Y >= Min.Y - slack
            && inner.Max.X <= Max.X + slack
            && inner.Max.Y <= Max.Y + slack;

        public static Box From(Vector2 pos, Vector2 size) => new(pos, new Vector2(pos.X + size.X, pos.Y + size.Y));

        public override string ToString() => Min + " " + Max;
    }

    public readonly record struct Fit(float LeftWidth, float RightWidth);

    public static Fit AssertFits(MainWindow window, float scale, string what)
    {
        var slack = 2f * scale;
        var screen = Screen();
        var shell = RequireShell(screen, what);
        Assert.True(screen.Contains(shell.Bounds, slack), what + ": shell " + shell.Bounds + " leaves " + screen);
        var panels = RequirePanels(shell.Id, what);
        AssertHalf(screen, panels.Left, true, slack, what + ": left panel");
        AssertHalf(screen, panels.Right, false, slack, what + ": right panel");
        var headers = NameHeaders(scale, what);
        Assert.Equal(2, headers.Count);
        AssertHalf(screen, headers[0], true, slack, what + ": left Name");
        AssertHalf(screen, headers[1], false, slack, what + ": right Name");
        AssertBars(window, shell, screen, slack, what);
        return new Fit(panels.Left.Width, panels.Right.Width);
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

    public static bool TryFilePoint(int index, out Vector2 point) => TryRowPoint(true, index, out point);

    public static bool TryRowInRightHalf(int index)
    {
        if (!TryRowPoint(false, index, out var point))
        {
            return false;
        }

        Vector2 size = ImGui.GetIO().DisplaySize;
        return point.X > size.X * 0.5f;
    }

    public static bool TryBrowsePoint(bool right, out Vector2 point)
    {
        point = default;
        var shell = FindShell();
        if (shell.IsNull)
        {
            return false;
        }

        var panels = ChildPanels(shell.ID);
        if (panels.Count != 2)
        {
            return false;
        }

        var panel = right ? panels[1] : panels[0];
        ImRect inner = panel.InnerRect;
        var style = ImGui.GetStyle();
        var button = ImGui.CalcTextSize("Browse").X + style.FramePadding.X * 2f;
        point = new Vector2(inner.Max.X - button * 0.5f, inner.Min.Y + ImGui.GetFrameHeight() * 0.5f);
        return true;
    }

    public static IReadOnlyList<Box> NoAutoMergeWindows()
    {
        var found = new List<Box>();
        var frame = ImGui.GetCurrentContext().FrameCount;
        var windows = ImGui.GetCurrentContext().Windows;
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows[i];
            if (window.IsNull || window.IsFallbackWindow || window.LastFrameActive != frame)
            {
                continue;
            }

            if (!window.ParentWindow.IsNull)
            {
                continue;
            }

            var flags = window.WindowClass.ViewportFlagsOverrideSet;
            if ((flags & ImGuiViewportFlags.NoAutoMerge) == 0)
            {
                continue;
            }

            Vector2 pos = window.Pos;
            Vector2 size = window.Size;
            found.Add(Box.From(pos, size));
        }

        return found;
    }

    private static void AssertBars(MainWindow window, ShellHit shell, Box screen, float slack, string what)
    {
        Assert.True(shell.MenuBar > 0f, what + ": menu bar was not drawn");
        var bar = new Box(shell.Bounds.Min, new Vector2(shell.Bounds.Max.X, shell.Bounds.Min.Y + shell.MenuBar));
        Assert.True(screen.Contains(bar, slack), what + ": menu bar " + bar);
        AssertMenu(window, shell, screen, slack, "Files", what);
        AssertMenu(window, shell, screen, slack, "Help", what);
        var keys = KeyBar(shell);
        Assert.True(screen.Contains(keys, slack), what + ": function keys " + keys + " leave " + screen);
        Assert.StartsWith("F3 ", window.FunctionKeys[0].Content, StringComparison.Ordinal);
        Assert.StartsWith("F8 ", window.FunctionKeys[5].Content, StringComparison.Ordinal);
        Assert.StartsWith("Alt+F4 ", window.FunctionKeys[7].Content, StringComparison.Ordinal);
    }

    private static void AssertMenu(MainWindow window, ShellHit shell, Box screen, float slack, string header, string what)
    {
        var style = ImGui.GetStyle();
        var x = shell.Bounds.Min.X + style.WindowPadding.X;
        var found = false;
        foreach (var menu in window.Menus)
        {
            var width = ImGui.CalcTextSize(menu.Header).X + style.FramePadding.X * 2f;
            if (menu.Header == header)
            {
                var item = new Box(
                    new Vector2(x, shell.Bounds.Min.Y),
                    new Vector2(x + width, shell.Bounds.Min.Y + shell.MenuBar));
                Assert.True(screen.Contains(item, slack), what + ": " + header + " at " + item);
                found = true;
            }

            x += width + style.ItemSpacing.X;
        }

        Assert.True(found, what + ": menu " + header + " was not drawn");
    }

    private static void AssertHalf(Box screen, Box item, bool left, float slack, string what)
    {
        var mid = screen.Center.X;
        var half = left
            ? new Box(screen.Min, new Vector2(mid, screen.Max.Y))
            : new Box(new Vector2(mid, screen.Min.Y), screen.Max);
        Assert.True(half.Contains(item, slack), what + " " + item + " spills out of " + half);
    }

    private static Box Screen()
    {
        Vector2 size = ImGui.GetIO().DisplaySize;
        return new Box(Vector2.Zero, size);
    }

    private static ShellHit RequireShell(Box screen, string what)
    {
        var shell = FindShell();
        Assert.False(shell.IsNull, what + ": main window was not drawn on " + screen);
        Vector2 pos = shell.Pos;
        Vector2 size = shell.Size;
        return new ShellHit(shell.ID, Box.From(pos, size), shell.MenuBarHeight);
    }

    private static (Box Left, Box Right) RequirePanels(uint shellId, string what)
    {
        var panels = ChildPanels(shellId);
        Assert.True(panels.Count == 2, what + ": expected two panels, found " + panels.Count);
        return (Bounds(panels[0]), Bounds(panels[1]));
    }

    private static bool TryRowPoint(bool left, int index, out Vector2 point)
    {
        point = default;
        var lists = ListBodies();
        if (lists.Count != 2)
        {
            return false;
        }

        var body = left ? lists[0] : lists[1];
        // Frozen header, then one row of padding before entry 0. One row higher selects the previous file.
        var header = HeaderHeight() + LayoutSession.RowHeight;
        var y = body.Min.Y + header + index * LayoutSession.RowHeight + LayoutSession.RowHeight * 0.5f;
        point = new Vector2(body.Min.X + 28f, y);
        return y >= body.Min.Y && y <= body.Max.Y;
    }

    private static List<Box> NameHeaders(float scale, string what)
    {
        var lists = ListBodies();
        var header = HeaderHeight();
        Assert.True(header <= 40f * scale, what + ": Name header is " + header + " px tall");
        var headers = new List<Box>();
        foreach (var body in lists)
        {
            headers.Add(new Box(body.Min, new Vector2(body.Max.X, body.Min.Y + header)));
        }

        return headers;
    }

    private static Box KeyBar(ShellHit shell)
    {
        var bottom = shell.Bounds.Min.Y;
        foreach (var panel in ChildPanels(shell.Id))
        {
            var bounds = Bounds(panel);
            if (bounds.Max.Y > bottom)
            {
                bottom = bounds.Max.Y;
            }
        }

        var style = ImGui.GetStyle();
        var top = shell.Bounds.Max.Y - style.WindowPadding.Y - 28f;
        if (top < bottom)
        {
            top = bottom;
        }

        return new Box(
            new Vector2(shell.Bounds.Min.X + style.WindowPadding.X, top),
            new Vector2(shell.Bounds.Max.X - style.WindowPadding.X, shell.Bounds.Max.Y - style.WindowPadding.Y));
    }

    private static List<Box> ListBodies()
    {
        var shell = FindShell();
        var lists = new List<Box>();
        if (shell.IsNull)
        {
            return lists;
        }

        foreach (var panel in ChildPanels(shell.ID))
        {
            var inner = ChildPanels(panel.ID);
            if (inner.Count == 0)
            {
                continue;
            }

            lists.Add(Bounds(inner[0]));
        }

        lists.Sort((left, right) => left.Min.X.CompareTo(right.Min.X));
        return lists;
    }

    private static List<ImGuiWindowPtr> ChildPanels(uint shellId)
    {
        var frame = ImGui.GetCurrentContext().FrameCount;
        var windows = ImGui.GetCurrentContext().Windows;
        var found = new List<ImGuiWindowPtr>();
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows[i];
            if (window.IsNull || window.LastFrameActive != frame || window.ParentWindow.IsNull)
            {
                continue;
            }

            if (window.ParentWindow.ID != shellId)
            {
                continue;
            }

            Vector2 size = window.Size;
            if (size.X < 40f || size.Y < 40f)
            {
                continue;
            }

            found.Add(window);
        }

        found.Sort((left, right) =>
        {
            Vector2 a = left.Pos;
            Vector2 b = right.Pos;
            return a.X.CompareTo(b.X);
        });
        return found;
    }

    private static ImGuiWindowPtr FindShell()
    {
        var frame = ImGui.GetCurrentContext().FrameCount;
        var windows = ImGui.GetCurrentContext().Windows;
        ImGuiWindowPtr shell = default;
        var best = -1f;
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows[i];
            if (window.IsNull || window.IsFallbackWindow || window.LastFrameActive != frame)
            {
                continue;
            }

            if (!window.ParentWindow.IsNull || window.MenuBarHeight <= 0f)
            {
                continue;
            }

            Vector2 size = window.Size;
            if (size.X * size.Y > best)
            {
                best = size.X * size.Y;
                shell = window;
            }
        }

        return shell;
    }

    private static Box Bounds(ImGuiWindowPtr window)
    {
        Vector2 pos = window.Pos;
        Vector2 size = window.Size;
        return Box.From(pos, size);
    }

    private static float HeaderHeight()
    {
        var style = ImGui.GetStyle();
        return ImGui.GetTextLineHeight() + style.CellPadding.Y * 2f;
    }

    private readonly record struct ShellHit(uint Id, Box Bounds, float MenuBar);
}
