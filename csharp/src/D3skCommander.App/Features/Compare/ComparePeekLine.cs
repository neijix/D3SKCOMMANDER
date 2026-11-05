using System.Globalization;
using System.Text;
using Avalonia.Media;
using D3skCommander.Core.Compare;

namespace D3skCommander.App.Features.Compare;

/// <summary>One hex line of the binary peek; tinted when it holds the first difference or differs.</summary>
public sealed record ComparePeekLine(string Text, IBrush? Brush)
{
    private static readonly IBrush First = new SolidColorBrush(Color.FromRgb(255, 89, 89));
    private static readonly IBrush Differs = new SolidColorBrush(Color.FromRgb(255, 140, 102));

    /// <summary>The lines of one side; <paramref name="mine"/> is that side, <paramref name="other"/> the opposite one.</summary>
    public static List<ComparePeekLine> Side(BinaryPeek peek, byte[] mine, byte[] other)
    {
        ArgumentNullException.ThrowIfNull(peek);
        var lines = new List<ComparePeekLine>();
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
            IBrush? brush = null;
            if (offset <= peek.First && peek.First < offset + width)
            {
                brush = First;
            }
            else if (SliceDiffers(mine, other, start, width))
            {
                brush = Differs;
            }

            lines.Add(new ComparePeekLine(HexLine(offset, mine, start, count), brush));
        }

        return lines;
    }

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

    private static int ByteAt(byte[] data, int index) => index < 0 || index >= data.Length ? -1 : data[index];

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
