using System.Globalization;
using System.Numerics;
using System.Text;
using D3skCommander.Core.Compare;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Compare;

public sealed partial class CompareView
{
    private void DrawBinary(CompareOutcome outcome)
    {
        if (outcome.Identical)
        {
            SurfaceText = _text.Get("compare.identical");
            ImGui.TextUnformatted(SurfaceText);
            return;
        }

        if (outcome.FirstDifference is long offset)
        {
            SurfaceText = CompareText.Offset(_text, offset);
            ImGui.TextWrapped(SurfaceText);
        }

        if (outcome.Peek is not null)
        {
            DrawPeek(outcome.Peek);
        }
    }

    private void DrawPeek(BinaryPeek peek)
    {
        DrewPeek = true;
        var height = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 4, ImGui.GetContentRegionAvail().Y);
        if (!ImGui.BeginTable("compare-peek", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, new Vector2(0, height)))
        {
            return;
        }

        ImGui.TableNextColumn();
        DrawPeekSide(_text.Get("compare.left"), peek.Left, peek.Right, peek);
        ImGui.TableNextColumn();
        DrawPeekSide(_text.Get("compare.right"), peek.Right, peek.Left, peek);
        ImGui.EndTable();
    }

    private static void DrawPeekSide(string title, byte[] mine, byte[] other, BinaryPeek peek)
    {
        ImGui.TextUnformatted(title);
        var width = DiffNavigator.RowBytes;
        for (var row = 0; row < DiffNavigator.PeekRows; row++)
        {
            var start = row * width;
            if (start >= mine.Length && start >= other.Length)
            {
                break;
            }

            var count = Math.Max(0, Math.Min(width, mine.Length - start));
            var offset = peek.Start + start;
            var line = HexLine(offset, mine, start, count);
            if (HitsFirst(offset, width, peek.First))
            {
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), line);
            }
            else if (SliceDiffers(mine, other, start, width))
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.4f, 1f), line);
            }
            else
            {
                ImGui.TextUnformatted(line);
            }
        }
    }

    private static bool HitsFirst(long offset, int width, long first) => offset <= first && first < offset + width;

    private static bool SliceDiffers(byte[] mine, byte[] other, int start, int width)
    {
        for (var i = 0; i < width; i++)
        {
            if (ByteAt(mine, start + i) != ByteAt(other, start + i))
            {
                return true;
            }
        }

        return false;
    }

    private static int ByteAt(byte[] data, int index)
    {
        if (index < 0 || index >= data.Length)
        {
            return -1;
        }

        return data[index];
    }

    private static string HexLine(long offset, byte[] data, int start, int count)
    {
        var builder = new StringBuilder(8 + (count * 3));
        builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
        for (var i = 0; i < count; i++)
        {
            builder.Append(' ');
            builder.Append(data[start + i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
