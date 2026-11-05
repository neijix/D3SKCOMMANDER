using System.Globalization;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Splitter;

/// <summary>Binary sizes: k = 1024. <c>1.44M</c>, <c>700MB</c>, <c>4G</c>, <c>100k</c>, <c>512</c>.</summary>
public static class SplitSize
{
    public static ParsedSize Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        var end = NumberEnd(trimmed);
        var unit = trimmed[end..];
        if (!TryMultiplier(unit, out var multiplier))
        {
            return ParsedSize.Fail(OperationError.InvalidData(Invalid(text)));
        }

        var number = trimmed[..end].Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return ParsedSize.Fail(OperationError.InvalidData(Invalid(text)));
        }

        var bytes = Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(bytes) || bytes >= ulong.MaxValue)
        {
            return ParsedSize.Fail(OperationError.InvalidData($"size too large: \"{text}\""));
        }

        return ParsedSize.Ok((ulong)bytes);
    }

    private static int NumberEnd(string text)
    {
        var end = 0;
        while (end < text.Length && text[end] is >= '0' and <= '9' or '.' or ',')
        {
            end++;
        }

        return end;
    }

    private static bool TryMultiplier(string unit, out double multiplier)
    {
        switch (unit.Trim().ToLowerInvariant())
        {
            case "":
            case "b":
                multiplier = 1d;
                return true;
            case "k":
            case "kb":
            case "kib":
                multiplier = 1024d;
                return true;
            case "m":
            case "mb":
            case "mib":
                multiplier = 1024d * 1024d;
                return true;
            case "g":
            case "gb":
            case "gib":
                multiplier = 1024d * 1024d * 1024d;
                return true;
            case "t":
            case "tb":
            case "tib":
                multiplier = 1024d * 1024d * 1024d * 1024d;
                return true;
            default:
                multiplier = 0d;
                return false;
        }
    }

    // v0.1 formats the rejected text with Rust's debug string (quoted).
    private static string Invalid(string text) => $"invalid size: \"{text}\"";
}
