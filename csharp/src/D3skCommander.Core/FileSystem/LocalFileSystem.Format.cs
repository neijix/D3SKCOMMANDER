using System.Globalization;

namespace D3skCommander.Core.FileSystem;

public static partial class LocalFileSystem
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>"1.5 KB", "12.0 MB", "3 B".</summary>
    public static string FormatSize(ulong bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024.0 && unit < SizeUnits.Length - 1)
        {
            value /= 1024.0;
            unit++;
        }

        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + SizeUnits[unit];
    }

    /// <summary>"1 234 567", grouped with a space (counts, and exact byte totals beside a short size).</summary>
    public static string FormatGrouped(ulong value)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        var chars = new char[digits.Length + (digits.Length / 3)];
        var at = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                chars[at] = ' ';
                at++;
            }

            chars[at] = digits[i];
            at++;
        }

        return new string(chars, 0, at);
    }

    /// <summary>Local "YYYY-MM-DD HH:MM", or "" when unknown.</summary>
    public static string FormatTime(DateTimeOffset? time)
    {
        if (time is null)
        {
            return "";
        }

        return time.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
