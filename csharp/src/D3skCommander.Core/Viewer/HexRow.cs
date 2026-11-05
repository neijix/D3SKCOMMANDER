using System.Globalization;
using System.Text;

namespace D3skCommander.Core.Viewer;

/// <summary><c>OFFSET  hh hh … hh  |ascii|</c> for up to 16 bytes. A short chunk is padded.</summary>
public static class HexRow
{
    public static string Format(int offset, ReadOnlySpan<byte> chunk)
    {
        var builder = new StringBuilder(8 + 2 + (ViewerLimits.HexWidth * 3) + 3 + chunk.Length);
        builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
        builder.Append(' ');
        AppendHex(builder, chunk);
        builder.Append(" |");
        AppendAscii(builder, chunk);
        builder.Append('|');
        return builder.ToString();
    }

    private static void AppendHex(StringBuilder builder, ReadOnlySpan<byte> chunk)
    {
        for (var i = 0; i < ViewerLimits.HexWidth; i++)
        {
            if (i % 8 == 0)
            {
                builder.Append(' ');
            }

            if (i < chunk.Length)
            {
                builder.Append(chunk[i].ToString("X2", CultureInfo.InvariantCulture));
                builder.Append(' ');
            }
            else
            {
                builder.Append("   ");
            }
        }
    }

    private static void AppendAscii(StringBuilder builder, ReadOnlySpan<byte> chunk)
    {
        for (var i = 0; i < chunk.Length; i++)
        {
            builder.Append(Printable(chunk[i]));
        }
    }

    private static char Printable(byte value)
    {
        if (value >= 0x20 && value < 0x7F)
        {
            return (char)value;
        }

        return '.';
    }
}
