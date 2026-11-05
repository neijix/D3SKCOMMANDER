using System.Numerics;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Catalog;

public sealed partial class CatalogDialog
{
    public void Draw()
    {
        if (!IsOpen)
        {
            return;
        }

        var title = _text.Get("cmd.catalog") + "###catalog";
        ImGui.OpenPopup(title);
        ImGui.SetNextWindowSize(WindowSize(), ImGuiCond.Appearing);
        var open = true;
        if (!ImGui.BeginPopupModal(title, ref open))
        {
            if (!open)
            {
                Close();
            }

            return;
        }

        DrawFields();
        DrawQuery();
        DrawHits();
        DrawActions();
        DrawStatus();
        ImGui.EndPopup();
        if (!open)
        {
            Close();
        }
    }

    private void DrawFields()
    {
        ImGui.TextUnformatted(_text.Get("catalog.root"));
        var root = Root;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##catalog-root", ref root, 4096))
        {
            Root = root;
        }

        ImGui.TextUnformatted(_text.Get("dlg.out_file"));
        var output = OutputPath;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##catalog-output", ref output, 4096))
        {
            OutputPath = output;
        }
    }

    private void DrawQuery()
    {
        ImGui.TextUnformatted(_text.Get("catalog.find"));
        var query = Query;
        var searchWidth = ImGui.CalcTextSize(_text.Get("catalog.search")).X + (ImGui.GetStyle().FramePadding.X * 2) + ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(Math.Max(ImGui.GetFrameHeight() * 8, ImGui.GetContentRegionAvail().X - searchWidth));
        if (ImGui.InputText("##catalog-find", ref query, 1024))
        {
            Query = query;
        }

        ImGui.SameLine();
        if (Action("catalog.search", !IsBusy))
        {
            _ = SearchAsync();
        }
    }

    private void DrawHits()
    {
        var line = ImGui.GetFrameHeightWithSpacing();
        var spare = line * 3;
        var avail = ImGui.GetContentRegionAvail().Y;
        _hits.ViewportHeight = Math.Max(line * 4, avail - spare);
        _hits.Draw();
        if (_hits.TakeActivation())
        {
            OpenSelected();
        }
    }

    private void DrawActions()
    {
        if (Action("catalog.create", !IsBusy))
        {
            _ = CreateAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button(_text.Get("catalog.open")))
        {
            OpenSelected();
        }

        ImGui.SameLine();
        if (ImGui.Button(_text.Get("close")))
        {
            Close();
        }
    }

    private void DrawStatus()
    {
        if (_status.Length > 0)
        {
            ImGui.TextWrapped(_status);
        }

        if (OpenedPath is null)
        {
            return;
        }

        ImGui.TextUnformatted(OpenedPath);
    }

    private bool Action(string key, bool enabled)
    {
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        var pressed = ImGui.Button(_text.Get(key));
        if (!enabled)
        {
            ImGui.EndDisabled();
        }

        return pressed && enabled;
    }

    private static Vector2 WindowSize()
    {
        var display = ImGui.GetIO().DisplaySize;
        var line = ImGui.GetFrameHeightWithSpacing();
        var width = Math.Max(line * 18f, display.X * 0.62f);
        var height = Math.Max(line * 14f, display.Y * 0.72f);
        return new Vector2(width, height);
    }
}
