using System.Globalization;
using System.Numerics;
using D3skCommander.Core.MultiRename;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.MultiRename;

public sealed partial class RenameWindow
{
    private static readonly (CaseMode Mode, string Key)[] Cases =
    [
        (CaseMode.Unchanged, "rename.case.unchanged"),
        (CaseMode.Lower, "rename.case.lower"),
        (CaseMode.Upper, "rename.case.upper"),
        (CaseMode.FirstUpper, "rename.case.first"),
        (CaseMode.TitleCase, "rename.case.title"),
    ];

    private void DrawForm()
    {
        if (!ImGui.BeginTable("rename-form", 2, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 12);
        ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch);
        DrawMaskRow();
        DrawTokenRow();
        EditRow("rename.extension", "##ext", () => _session.Rule.ExtMask, value => _session.Rule.ExtMask = value);
        EditRow("rename.search", "##search", () => _session.Rule.Search, value => _session.Rule.Search = value);
        EditRow("rename.replace", "##replace", () => _session.Rule.Replace, value => _session.Rule.Replace = value);
        DrawFlags();
        DrawCase();
        DrawCounter();
        ImGui.EndTable();
    }

    private void DrawMaskRow()
    {
        ImGui.TableNextRow();
        Label("rename.name");
        ImGui.TableNextColumn();
        var mask = _session.Rule.NameMask;
        var keepStart = _nameStart;
        var keepEnd = _nameEnd;
        var bumped = ReloadMask(mask);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var id = "##name-" + _maskEpoch.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText(id, ref mask, 1024, ImGuiInputTextFlags.CallbackAlways, _onMask)
            && !string.Equals(mask, _session.Rule.NameMask, StringComparison.Ordinal))
        {
            _session.Rule.NameMask = mask;
            Changed();
        }

        if (bumped)
        {
            _nameStart = keepStart;
            _nameEnd = keepEnd;
        }

        _seenMask = _session.Rule.NameMask;
    }

    private bool ReloadMask(string mask)
    {
        if (!_reloadMask && string.Equals(mask, _seenMask, StringComparison.Ordinal))
        {
            return false;
        }

        _maskEpoch++;
        _reloadMask = false;
        return true;
    }

    private void DrawTokenRow()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        DrawTokens();
    }

    private void DrawTokens()
    {
        _hits.Clear();
        Vector2 window = default;
        Vector2 size = default;
        ImGui.GetWindowPos(ref window);
        ImGui.GetWindowSize(ref size);
        var right = window.X + size.X - ImGui.GetStyle().WindowPadding.X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var pad = ImGui.GetStyle().FramePadding.X;
        var placed = false;
        foreach (var token in RenameToken.All)
        {
            var label = _text.Get(token.LabelKey);
            if (placed && Fits(label, right, spacing, pad))
            {
                ImGui.SameLine();
            }

            placed = true;
            if (ImGui.Button(label))
            {
                Insert(label);
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            {
                ImGui.SetItemTooltip(_text.Get(token.HintKey));
            }

            RememberHit(label);
        }
    }

    private static bool Fits(string label, float right, float spacing, float pad)
    {
        var width = ImGui.CalcTextSize(label).X + pad * 2;
        Vector2 last = default;
        ImGui.GetItemRectMax(ref last);
        return last.X + spacing + width < right;
    }

    private void RememberHit(string label)
    {
        Vector2 min = default;
        Vector2 max = default;
        ImGui.GetItemRectMin(ref min);
        ImGui.GetItemRectMax(ref max);
        _hits.Add(new RenameTokenHit(label, new Vector2((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f)));
    }

    private void EditRow(string labelKey, string id, Func<string> read, Action<string> write)
    {
        ImGui.TableNextRow();
        Label(labelKey);
        ImGui.TableNextColumn();
        var value = read();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (!ImGui.InputText(id, ref value, 1024))
        {
            return;
        }

        if (string.Equals(value, read(), StringComparison.Ordinal))
        {
            return;
        }

        write(value);
        Changed();
    }

    private void DrawFlags()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        var regex = _session.Rule.UseRegex;
        if (ImGui.Checkbox(_text.Get("rename.regex") + "##regex", ref regex))
        {
            _session.Rule.UseRegex = regex;
            Changed();
        }

        ImGui.SameLine();
        var sensitive = _session.Rule.CaseSensitive;
        if (ImGui.Checkbox(_text.Get("rename.case_sensitive") + "##sensitive", ref sensitive))
        {
            _session.Rule.CaseSensitive = sensitive;
            Changed();
        }
    }

    private void DrawCase()
    {
        ImGui.TableNextRow();
        Label("rename.case");
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (!ImGui.BeginCombo("##case", CaseText(_session.Rule.CaseMode)))
        {
            return;
        }

        foreach (var row in Cases)
        {
            var selected = row.Mode == _session.Rule.CaseMode;
            if (ImGui.Selectable(CaseText(row.Mode), selected) && !selected)
            {
                _session.Rule.CaseMode = row.Mode;
                Changed();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawCounter()
    {
        ImGui.TableNextRow();
        Label("rename.counter");
        ImGui.TableNextColumn();
        var start = ClampInt(_session.Rule.CounterStart);
        var step = ClampInt(_session.Rule.CounterStep);
        var digits = _session.Rule.CounterDigits;
        var changed = false;
        if (Number("rename.counter.start", ref start))
        {
            _session.Rule.CounterStart = start;
            changed = true;
        }

        ImGui.SameLine();
        if (Number("rename.counter.step", ref step))
        {
            _session.Rule.CounterStep = step;
            changed = true;
        }

        ImGui.SameLine();
        if (Number("rename.counter.digits", ref digits))
        {
            _session.Rule.CounterDigits = Math.Clamp(digits, 1, 12);
            changed = true;
        }

        if (changed)
        {
            Changed();
        }
    }

    private bool Number(string key, ref int value)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(_text.Get(key));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 6);
        return ImGui.InputInt("##" + key, ref value);
    }

    private void Label(string key)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(_text.Get(key));
    }

    private string CaseText(CaseMode mode)
    {
        foreach (var row in Cases)
        {
            if (row.Mode == mode)
            {
                return _text.Get(row.Key);
            }
        }

        return _text.Get("rename.case.unchanged");
    }

    private static int ClampInt(long value)
    {
        if (value > int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value < int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }
}
