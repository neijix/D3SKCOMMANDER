using System.Globalization;
using D3skCommander.Core.Compare;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Compare;

public sealed partial class CompareView
{
    private void DrawPaths()
    {
        var width = Math.Max(80f, (ImGui.GetContentRegionAvail().X - 180f) / 2f);
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##compare-left", ref _leftPath, 4096);
        ImGui.SameLine();
        if (ImGui.Button(_text.Get("compare.swap")))
        {
            SwapPaths();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##compare-right", ref _rightPath, 4096);
        ImGui.SameLine();
        if (ImGui.Button(_text.Get("compare.compare")))
        {
            RunCompare();
        }
    }

    private void DrawNavigation()
    {
        if (_session.Outcome.Kind != CompareKind.Text)
        {
            return;
        }

        DrewNext = true;
        if (ImGui.Button(_text.Get("compare.previous")))
        {
            PreviousBlock();
        }

        ImGui.SameLine();
        if (ImGui.Button(_text.Get("compare.next")))
        {
            NextBlock();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(BlockLabel());
    }

    private void DrawOptions()
    {
        if (_session.Outcome.Kind == CompareKind.Text)
        {
            ImGui.SameLine();
        }

        ToggleCase();
        ImGui.SameLine();
        ToggleSpace();
    }

    private void DrawModes()
    {
        if (_session.Outcome.Kind != CompareKind.Text && _session.LeftBuffer is null)
        {
            PendingMark();
            return;
        }

        if (ImGui.Button(_text.Get("compare.view")))
        {
            ShowCompareView();
        }

        ImGui.SameLine();
        if (ImGui.Button(_text.Get("cmd.edit")))
        {
            BeginEdit();
        }

        if (!ShowingEdit)
        {
            PendingMark();
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button(_text.Get("compare.recompare")))
        {
            _session.RecompareBuffers();
        }

        ImGui.SameLine();
        DrewSave = true;
        if (ImGui.Button(_text.Get("compare.save_left")))
        {
            _session.Save(0);
        }

        ImGui.SameLine();
        if (ImGui.Button(_text.Get("compare.save_right")))
        {
            _session.Save(1);
        }

        PendingMark();
    }

    private void PendingMark()
    {
        if (!_session.HasPendingResult)
        {
            return;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(_text.Get("compare.working"));
    }

    private void DrawStatus()
    {
        StatusLine = CompareText.Status(_text, _session.Status);
        if (StatusLine.Length > 0)
        {
            ImGui.TextWrapped(StatusLine);
        }
    }

    private void SwapPaths()
    {
        var held = _leftPath;
        _leftPath = _rightPath;
        _rightPath = held;
        RunCompare();
    }

    private void RunCompare()
    {
        _forceView = false;
        _leftSource = null;
        _rightSource = null;
        _session.LeftPath = _leftPath.Trim();
        _session.RightPath = _rightPath.Trim();
        _session.CompareFiles();
    }

    private void ToggleCase()
    {
        var value = _session.Options.IgnoreCase;
        if (!ImGui.Checkbox(_text.Get("compare.ignore_case"), ref value))
        {
            return;
        }

        _session.Options = new DiffOptions(value, _session.Options.IgnoreWhitespace);
        Rerun();
    }

    private void ToggleSpace()
    {
        var value = _session.Options.IgnoreWhitespace;
        if (!ImGui.Checkbox(_text.Get("compare.ignore_whitespace"), ref value))
        {
            return;
        }

        _session.Options = new DiffOptions(_session.Options.IgnoreCase, value);
        Rerun();
    }

    private void Rerun()
    {
        if (ShowingEdit)
        {
            _session.Rerun();
            return;
        }

        RunCompare();
    }

    private string BlockLabel()
    {
        var count = _session.Outcome.Blocks.ToString(CultureInfo.InvariantCulture);
        return CompareText.Fill(_text.Get("compare.blocks"), "{n}", count);
    }
}
