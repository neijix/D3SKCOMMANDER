using System.Numerics;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using Hexa.NET.ImGui;
using Xunit;

namespace D3skCommander.App.Tests.Layout;

/// <summary>Mouse input for one probe. The press lands on the frame after the row is measured.</summary>
internal static class LayoutInput
{
    public static void PressFile(LayoutSession session, string prefix, bool ctrl, float shift, int frames)
    {
        var index = LayoutGeometry.EntryIndex(session.Read(PanelSide.Left), prefix);
        Vector2 at = default;
        var ready = false;
        var hold = frames < 1 ? 1 : frames;
        var count = hold + 2;
        ImGuiProbe.Frames(count, frame =>
        {
            session.Prepare(1280, 800, 1);
            if (!ready)
            {
                return;
            }

            var moved = frame > 1;
            var x = moved ? at.X + shift : at.X;
            var y = moved ? at.Y + shift * 0.5f : at.Y;
            var io = ImGui.GetIO();
            io.AddMousePosEvent(x, y);
            if (frame == 1)
            {
                if (ctrl)
                {
                    io.AddKeyEvent(ImGuiKey.ModCtrl, true);
                }

                io.AddMouseButtonEvent(0, true);
            }

            if (frame == count - 1)
            {
                io.AddMouseButtonEvent(0, false);
                if (ctrl)
                {
                    io.AddKeyEvent(ImGuiKey.ModCtrl, false);
                }
            }
        }, () =>
        {
            session.Draw(false);
            if (!ready)
            {
                ready = LayoutGeometry.TryFilePoint(index, out at);
            }
        });

        Assert.True(ready, prefix + " was not drawn");
    }

    public static void ClickBrowse(LayoutSession session, bool right)
    {
        Vector2 at = default;
        var ready = false;
        ImGuiProbe.Frames(3, frame =>
        {
            session.Prepare(1280, 800, 1);
            if (!ready)
            {
                return;
            }

            var io = ImGui.GetIO();
            io.AddMousePosEvent(at.X, at.Y);
            if (frame == 1)
            {
                io.AddMouseButtonEvent(0, true);
            }

            if (frame == 2)
            {
                io.AddMouseButtonEvent(0, false);
            }
        }, () =>
        {
            session.Draw(false);
            if (!ready)
            {
                ready = LayoutGeometry.TryBrowsePoint(right, out at);
            }
        });

        Assert.True(ready, "the browse button was not drawn");
    }
}
