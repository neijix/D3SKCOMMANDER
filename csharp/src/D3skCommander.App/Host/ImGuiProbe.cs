using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;

namespace D3skCommander.App.Host;

/// <summary>One ImGui frame without a window, so tests can run the same draw path the GLFW host runs.</summary>
public static class ImGuiProbe
{
    private static readonly object Gate = new();

    public static void Draw(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        lock (Gate)
        {
            var gui = ImGui.CreateContext();
            var plot = ImPlot.CreateContext();
            try
            {
                ImGui.SetCurrentContext(gui);
                ImPlot.SetCurrentContext(plot);
                ImGuiContextFactory.Configure(1, viewports: false);
                var io = ImGui.GetIO();
                io.DisplaySize = new Vector2(1100, 700);
                // No GLFW renderer here. 1.92 asserts unless a backend owns the font texture.
                io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
                ImGui.NewFrame();
                draw();
                ImGui.Render();
            }
            finally
            {
                ImPlot.DestroyContext(plot);
                ImGui.SetCurrentContext(null);
                ImPlot.SetCurrentContext(null);
                ImGui.DestroyContext(gui);
            }
        }
    }
}
