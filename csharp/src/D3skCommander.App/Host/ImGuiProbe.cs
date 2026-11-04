using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;

namespace D3skCommander.App.Host;

/// <summary>One ImGui frame without a window, so tests can run the same draw path the GLFW host runs.</summary>
public static class ImGuiProbe
{
    private static readonly object Gate = new();

    public static void Draw(Action draw) => Frames(1, null, draw);

    /// <summary><paramref name="beforeFrame"/> runs before <c>NewFrame</c>, so mouse events land in that frame.</summary>
    public static void Frames(int count, Action<int>? beforeFrame, Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
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
                for (var frame = 0; frame < count; frame++)
                {
                    beforeFrame?.Invoke(frame);
                    ImGui.NewFrame();
                    draw();
                    ImGui.Render();
                }
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
