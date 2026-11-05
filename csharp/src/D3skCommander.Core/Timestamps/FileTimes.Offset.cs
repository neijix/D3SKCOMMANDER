using System.Globalization;

namespace D3skCommander.Core.Timestamps;

public static partial class FileTimes
{
    /// <summary>
    /// An offset in seconds: <c>+1h30m</c>, <c>-2d</c>, <c>90s</c>, <c>1:30:00</c> (h:m:s), <c>-0:05</c> (h:m).
    /// Units are <c>w</c>, <c>d</c>, <c>h</c>, <c>m</c>, <c>s</c>. A bare number is seconds. A leading sign sets the direction.
    /// </summary>
    public static OffsetResult ParseOffset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        var negative = false;
        var body = trimmed;
        if (trimmed.Length > 0 && trimmed[0] is '-' or '+')
        {
            negative = trimmed[0] == '-';
            body = trimmed[1..].Trim();
        }

        if (body.Length == 0)
        {
            return OffsetResult.Fail($"empty time offset: {Quote(text)}");
        }

        if (!(body.Contains(':') ? TryParseClock(body, out var seconds) : TryParseUnits(body, out seconds)))
        {
            return OffsetResult.Fail($"invalid time offset: {Quote(body)}");
        }

        if (seconds > long.MaxValue)
        {
            return OffsetResult.Fail($"time offset too large: {Quote(text)}");
        }

        var signed = (long)seconds;
        return OffsetResult.Ok(negative ? -signed : signed);
    }

    private static bool TryParseClock(string body, out ulong seconds)
    {
        seconds = 0;
        var parts = body.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!TryParsePart(parts[0], out var hours) || !TryParsePart(parts[1], out var minutes))
        {
            return false;
        }

        ulong secs = 0;
        if (parts.Length == 3 && !TryParsePart(parts[2], out secs))
        {
            return false;
        }

        seconds = SaturatingAdd(SaturatingAdd(SaturatingMul(hours, 3600), SaturatingMul(minutes, 60)), secs);
        return true;
    }

    private static bool TryParseUnits(string body, out ulong total)
    {
        total = 0;
        var start = -1;
        var seen = false;
        for (var i = 0; i <= body.Length; i++)
        {
            var c = i == body.Length ? '\0' : body[i];
            if (c is >= '0' and <= '9')
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start < 0 && (c == '\0' || char.IsWhiteSpace(c)))
            {
                if (c == '\0')
                {
                    break;
                }

                continue;
            }

            if (start < 0 || !UnitSeconds(c, out var unit))
            {
                return false;
            }

            if (!ulong.TryParse(body.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            total = SaturatingAdd(total, SaturatingMul(number, unit));
            start = -1;
            seen = true;
        }

        return seen;
    }

    private static bool TryParsePart(string part, out ulong value) =>
        ulong.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    // A trailing bare number is seconds. Letter units are case-insensitive.
    private static bool UnitSeconds(char c, out ulong unit)
    {
        switch (c)
        {
            case '\0':
                unit = 1;
                return true;
            case 'w':
            case 'W':
                unit = 604_800;
                return true;
            case 'd':
            case 'D':
                unit = 86_400;
                return true;
            case 'h':
            case 'H':
                unit = 3_600;
                return true;
            case 'm':
            case 'M':
                unit = 60;
                return true;
            case 's':
            case 'S':
                unit = 1;
                return true;
            default:
                unit = 0;
                return false;
        }
    }

    private static ulong SaturatingMul(ulong left, ulong right)
    {
        if (left == 0 || right == 0)
        {
            return 0;
        }

        return left > ulong.MaxValue / right ? ulong.MaxValue : left * right;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;

    private static string Quote(string text) =>
        $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
