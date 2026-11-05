using System.Text;
using System.Text.RegularExpressions;

namespace D3skCommander.Core.MultiRename;

public static partial class MultiRename
{
    // The default engine backtracks; Rust's matcher did not.
    private const RegexOptions Engine = RegexOptions.NonBacktracking;

    private static bool TrySearch(string name, RenameRule rule, out string replaced, out string? error)
    {
        replaced = name;
        error = null;
        if (rule.Search.Length == 0)
        {
            return true;
        }

        var pattern = rule.UseRegex ? rule.Search : Regex.Escape(rule.Search);
        var options = Engine;
        if (!rule.CaseSensitive)
        {
            options |= RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        }

        try
        {
            var regex = new Regex(pattern, options);
            if (rule.UseRegex)
            {
                replaced = regex.Replace(name, rule.Replace);
            }
            else
            {
                var literal = rule.Replace;
                replaced = regex.Replace(name, _ => literal);
            }

            return true;
        }
        catch (Exception ex) when (ex is RegexParseException or ArgumentException)
        {
            error = "invalid search pattern: " + ex.Message;
            return false;
        }
    }

    private static string ApplyCase(string name, CaseMode mode)
    {
        if (mode == CaseMode.Lower)
        {
            return name.ToLowerInvariant();
        }

        if (mode == CaseMode.Upper)
        {
            return name.ToUpperInvariant();
        }

        if (mode == CaseMode.FirstUpper)
        {
            return FirstUpper(name);
        }

        if (mode == CaseMode.TitleCase)
        {
            return TitleCase(name);
        }

        return name;
    }

    private static string FirstUpper(string name)
    {
        var output = new StringBuilder(name.Length);
        var first = true;
        foreach (var rune in name.EnumerateRunes())
        {
            var text = rune.ToString();
            output.Append(first ? text.ToUpperInvariant() : text.ToLowerInvariant());
            first = false;
        }

        return output.ToString();
    }

    // A letter starts a word. An apostrophe keeps the current word; anything else ends it.
    private static string TitleCase(string name)
    {
        var output = new StringBuilder(name.Length);
        var inWord = false;
        foreach (var rune in name.EnumerateRunes())
        {
            var text = rune.ToString();
            output.Append(inWord ? text.ToLowerInvariant() : text.ToUpperInvariant());
            var apostrophe = rune.Value == '\'';
            inWord = Rune.IsLetterOrDigit(rune) || (inWord && apostrophe);
        }

        return output.ToString();
    }

    private static string? InvalidName(string name)
    {
        if (name.Length == 0)
        {
            return "the new name is empty";
        }

        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.IsBmp && IsForbidden((char)rune.Value))
            {
                return "the new name contains an invalid character ('" + (char)rune.Value + "')";
            }
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return "the new name ends with a dot or a space";
        }

        return null;
    }

    private static bool IsForbidden(char c) => "<>:\"/\\|?*".Contains(c) || char.IsControl(c);
}
