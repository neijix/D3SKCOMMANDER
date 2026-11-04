using Hexa.NET.ImGui;

namespace D3skCommander.App.Host;

/// <summary>Shared ImGui options for the GLFW host and the headless probe.</summary>
internal static class ImGuiContextFactory
{
    public static void Configure(float scale, bool viewports)
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        if (viewports)
        {
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        }

        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        var factor = scale <= 0 ? 1 : scale;
        style.ScaleAllSizes(factor);
        style.FontScaleDpi = factor;
        io.ConfigDpiScaleFonts = true;
        io.ConfigDpiScaleViewports = true;
        if (viewports)
        {
            style.WindowRounding = 0;
            style.Colors[(int)ImGuiCol.WindowBg].W = 1;
        }
    }
}
