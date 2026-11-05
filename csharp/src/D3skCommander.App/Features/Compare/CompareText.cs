using System.Globalization;
using D3skCommander.Core.Compare;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Compare;

/// <summary>Fills compare catalog strings. Status text comes from <see cref="CompareStatusKind"/>.</summary>
public static class CompareText
{
    public static string Status(ILocalizer text, CompareStatus status)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (status.Kind == CompareStatusKind.Saved)
        {
            return Fill(text.Get("compare.status.saved"), "{path}", status.Path);
        }

        if (status.Kind == CompareStatusKind.TooLarge)
        {
            return Fill(text.Get("compare.status.too_large"), "{path}", status.Path);
        }

        if (status.Kind == CompareStatusKind.Error)
        {
            return Fill(text.Get("compare.status.error"), "{detail}", status.Detail);
        }

        return "";
    }

    public static string Counts(ILocalizer text, int left, int right)
    {
        ArgumentNullException.ThrowIfNull(text);
        var withLeft = Fill(text.Get("compare.panels.counts"), "{left}", left.ToString(CultureInfo.InvariantCulture));
        return Fill(withLeft, "{right}", right.ToString(CultureInfo.InvariantCulture));
    }

    public static string Offset(ILocalizer text, long offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        var withCount = Fill(text.Get("compare.binary_at"), "{n}", offset.ToString(CultureInfo.InvariantCulture));
        return Fill(withCount, "{hex}", "0x" + offset.ToString("X", CultureInfo.InvariantCulture));
    }

    public static string Fill(string template, string token, string value)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(value);
        return template.Replace(token, value, StringComparison.Ordinal);
    }
}
