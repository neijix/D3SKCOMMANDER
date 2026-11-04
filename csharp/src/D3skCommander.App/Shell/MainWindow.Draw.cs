using System.Numerics;
using D3skCommander.Core.Commands;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Shell;

public sealed partial class MainWindow
{
    public void Draw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        var flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.MenuBar;
        ImGui.Begin("##shell", flags);
        DrawMenu();
        DrawPanels();
        DrawRegion(UnderPanels);
        ImGui.TextUnformatted(StatusText);
        DrawFunctionKeys();
        ImGui.End();
    }

    private void DrawMenu()
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }

        foreach (var menu in Menus)
        {
            if (!ImGui.BeginMenu(menu.Header))
            {
                continue;
            }

            foreach (var entry in menu.Items)
            {
                DrawEntry(entry);
            }

            ImGui.EndMenu();
        }

        foreach (var extra in MenuBarTrailing)
        {
            extra.Draw();
        }

        ImGui.EndMenuBar();
    }

    private void DrawEntry(ShellMenuEntry entry)
    {
        if (entry.IsSeparator)
        {
            ImGui.Separator();
            return;
        }

        var shortcut = entry.Gesture is Gesture gesture ? gesture.Format() : "";
        if (ImGui.MenuItem(entry.Header, shortcut, false, entry.IsEnabled) && entry.Command is CommandId command)
        {
            _shell.Dispatch(command);
        }
    }

    private void DrawPanels()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var beside = BesidePanels.Count > 0 ? 180 : 0;
        var panelWidth = Math.Max(40, (width - beside) / 2);
        var height = Math.Max(80, ImGui.GetContentRegionAvail().Y - 64);
        DrawSlot(LeftPanel, panelWidth, height);
        ImGui.SameLine();
        DrawSlot(RightPanel, panelWidth, height);
        if (beside > 0)
        {
            ImGui.SameLine();
            DrawRegion(BesidePanels);
        }
    }

    private static void DrawSlot(ShellSlot slot, float width, float height)
    {
        ImGui.BeginChild(slot.Name, new Vector2(width, height), ImGuiChildFlags.Borders);
        slot.Content?.Draw();
        ImGui.EndChild();
    }

    private static void DrawRegion(IReadOnlyList<IShellWidget> widgets)
    {
        foreach (var widget in widgets)
        {
            widget.Draw();
        }
    }

    private void DrawFunctionKeys()
    {
        if (FunctionKeys.Count == 0 || !ImGui.BeginTable("keys", FunctionKeys.Count, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        foreach (var button in FunctionKeys)
        {
            ImGui.TableNextColumn();
            if (!button.IsEnabled)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button(button.Content, new Vector2(-1, 28)))
            {
                button.Click();
            }

            if (!button.IsEnabled)
            {
                ImGui.EndDisabled();
            }
        }

        ImGui.EndTable();
    }
}
