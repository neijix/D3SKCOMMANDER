using System.Globalization;
using System.Text;

namespace D3skCommander.Core.MultiRename;

public static partial class MultiRename
{
    private static bool TryExpand(string mask, Ctx ctx, out string text, out string? error)
    {
        var output = new StringBuilder();
        var rest = mask;
        while (true)
        {
            var pos = rest.IndexOf('[');
            if (pos < 0)
            {
                output.Append(rest);
                text = output.ToString();
                error = null;
                return true;
            }

            output.Append(rest.AsSpan(0, pos));
            rest = rest[(pos + 1)..];
            if (rest.StartsWith('['))
            {
                output.Append('[');
                rest = rest[1..];
                continue;
            }

            var end = rest.IndexOf(']');
            if (end < 0)
            {
                text = "";
                error = "unterminated placeholder in \"" + mask + "\"";
                return false;
            }

            if (!TryToken(rest[..end], ctx, out var piece, out error))
            {
                text = "";
                return false;
            }

            output.Append(piece);
            rest = rest[(end + 1)..];
        }
    }

    private static bool TryToken(string tok, Ctx ctx, out string value, out string? error)
    {
        if (tok == "N")
        {
            value = ctx.Stem;
            error = null;
            return true;
        }

        if (tok == "E")
        {
            value = ctx.Ext;
            error = null;
            return true;
        }

        if (tok == "P")
        {
            value = AncestorName(ctx.File, 1);
            error = null;
            return true;
        }

        if (tok == "G")
        {
            value = AncestorName(ctx.File, 2);
            error = null;
            return true;
        }

        var format = DateFormat(tok);
        if (format is not null)
        {
            return TryDate(ctx.File, format, out value, out error);
        }

        return TryParametric(tok, ctx, out value, out error);
    }

    private static bool TryParametric(string tok, Ctx ctx, out string value, out string? error)
    {
        value = "";
        if (tok.Length == 0)
        {
            error = "unknown placeholder [" + tok + "]";
            return false;
        }

        var head = tok[0];
        var spec = tok[1..];
        if (head == 'N')
        {
            return TryRange(ctx.Stem, spec, tok, out value, out error);
        }

        if (head == 'E')
        {
            return TryRange(ctx.Ext, spec, tok, out value, out error);
        }

        if (head == 'C')
        {
            return TryCounter(spec, tok, ctx, out value, out error);
        }

        error = "unknown placeholder [" + tok + "]";
        return false;
    }

    private static bool TryRange(string text, string spec, string tok, out string value, out string? error)
    {
        value = "";
        if (spec.Length == 0)
        {
            value = text;
            error = null;
            return true;
        }

        var bad = "invalid range in placeholder [" + tok + "]";
        var dash = spec.IndexOf('-');
        var firstText = dash < 0 ? spec : spec[..dash];
        string? lastText = dash < 0 ? null : spec[(dash + 1)..];
        if (!int.TryParse(firstText, NumberStyles.None, CultureInfo.InvariantCulture, out var first) || first == 0)
        {
            error = bad;
            return false;
        }

        if (!TryRangeCount(lastText, first, out var count))
        {
            error = bad;
            return false;
        }

        value = TakeRunes(text, first - 1, count);
        error = null;
        return true;
    }

    private static bool TryRangeCount(string? lastText, int first, out int count)
    {
        if (lastText is null)
        {
            count = 1;
            return true;
        }

        if (lastText.Length == 0)
        {
            count = int.MaxValue;
            return true;
        }

        if (!int.TryParse(lastText, NumberStyles.None, CultureInfo.InvariantCulture, out var last) || last < first)
        {
            count = 0;
            return false;
        }

        count = last - first + 1;
        return true;
    }

    private static string TakeRunes(string text, int skip, int count)
    {
        var output = new StringBuilder();
        var index = 0;
        var taken = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (index >= skip)
            {
                if (taken >= count)
                {
                    break;
                }

                output.Append(rune);
                taken++;
            }

            index++;
        }

        return output.ToString();
    }

    private static bool TryCounter(string spec, string tok, Ctx ctx, out string value, out string? error)
    {
        value = "";
        var colon = spec.IndexOf(':');
        var rest = colon < 0 ? spec : spec[..colon];
        string? digitsText = colon < 0 ? null : spec[(colon + 1)..];
        var plus = rest.IndexOf('+');
        var startText = plus < 0 ? rest : rest[..plus];
        string? stepText = plus < 0 ? null : rest[(plus + 1)..];
        if (!TrySigned(startText, ctx.Rule.CounterStart, out var start)
            || !TrySigned(stepText, ctx.Rule.CounterStep, out var step)
            || !TryWidth(digitsText, ctx.Rule.CounterDigits, out var width))
        {
            error = "invalid counter placeholder [" + tok + "]";
            return false;
        }

        var amount = SaturatingAdd(start, SaturatingMul(step, ctx.Index));
        value = FormatCounter(amount, width);
        error = null;
        return true;
    }

    private static bool TrySigned(string? text, long fallback, out long value)
    {
        if (string.IsNullOrEmpty(text))
        {
            value = fallback;
            return true;
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryWidth(string? text, int fallback, out int value)
    {
        if (string.IsNullOrEmpty(text))
        {
            value = fallback;
            return true;
        }

        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed > int.MaxValue)
        {
            value = 0;
            return false;
        }

        value = (int)parsed;
        return true;
    }

    // Rust `{value:0width$}`: the sign counts toward the width, so -2 in width 3 is "-02".
    private static string FormatCounter(long value, int width)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (width <= text.Length)
        {
            return text;
        }

        if (value < 0)
        {
            var digits = text[1..];
            var zeros = width - digits.Length - 1;
            return zeros <= 0 ? text : "-" + new string('0', zeros) + digits;
        }

        return text.PadLeft(width, '0');
    }

    private static long SaturatingMul(long left, long right)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            if ((left < 0 && right < 0) || (left > 0 && right > 0))
            {
                return long.MaxValue;
            }

            return long.MinValue;
        }
    }

    private static long SaturatingAdd(long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            return right >= 0 ? long.MaxValue : long.MinValue;
        }
    }

    private static string? DateFormat(string tok)
    {
        if (tok == "Y")
        {
            return "yyyy";
        }

        if (tok == "M")
        {
            return "MM";
        }

        if (tok == "D")
        {
            return "dd";
        }

        if (tok == "h")
        {
            return "HH";
        }

        if (tok == "m")
        {
            return "mm";
        }

        if (tok == "s")
        {
            return "ss";
        }

        if (tok == "YMD")
        {
            return "yyyyMMdd";
        }

        if (tok == "hms")
        {
            return "HHmmss";
        }

        return null;
    }

    private static bool TryDate(string file, string format, out string value, out string? error)
    {
        value = "";
        if (!MetadataExists(file))
        {
            error = "cannot read the modification time of " + file + ": not found";
            return false;
        }

        try
        {
            var local = File.GetLastWriteTime(file);
            value = local.ToString(format, CultureInfo.InvariantCulture);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "cannot read the modification time of " + file + ": " + ex.Message;
            return false;
        }
    }

    private static string AncestorName(string file, int level)
    {
        var current = file;
        for (var i = 0; i < level; i++)
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                return "";
            }

            current = parent;
        }

        return Path.GetFileName(current);
    }

    private static string StemOf(string file) => Path.GetFileNameWithoutExtension(file);

    private static string ExtensionOf(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Length == 0 ? "" : ext[1..];
    }

    private static string WithFileName(string path, string name)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return name;
        }

        return Path.Combine(directory, name);
    }
}
