using System.Text;

namespace D3skCommander.Core.Compare;

/// <summary>Line splitting that matches Rust <c>str::lines</c>: a trailing newline adds no empty row, and a lone CR stays in the line.</summary>
internal static class TextLines
{
    public static List<string> Split(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            var end = i;
            if (end > start && text[end - 1] == '\r')
            {
                end--;
            }

            lines.Add(text[start..end]);
            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    public static string Normalize(string line, DiffOptions options)
    {
        var text = line;
        if (options.IgnoreWhitespace)
        {
            text = StripWhitespace(line);
        }

        if (options.IgnoreCase)
        {
            text = text.ToLowerInvariant();
        }

        return text;
    }

    private static string StripWhitespace(string line)
    {
        var builder = new StringBuilder(line.Length);
        foreach (var ch in line)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
