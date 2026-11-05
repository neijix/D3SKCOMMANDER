using System.Text;

namespace D3skCommander.Core.Compare;

/// <summary>Load and save the compare editor's text without changing its line endings or BOM.</summary>
internal static class LineEndings
{
    internal readonly record struct Decoded(string Text, LineEnding Ending, bool Bom);

    public static Decoded Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var body = bom ? bytes.AsSpan(3) : bytes.AsSpan();
        var raw = Encoding.UTF8.GetString(body);
        return new Decoded(ToLf(raw), Detect(raw), bom);
    }

    public static byte[] Encode(string text, LineEnding ending, bool bom)
    {
        ArgumentNullException.ThrowIfNull(text);
        var body = Encoding.UTF8.GetBytes(Restore(text, ending));
        if (!bom)
        {
            return body;
        }

        var output = new byte[body.Length + 3];
        output[0] = 0xEF;
        output[1] = 0xBB;
        output[2] = 0xBF;
        body.CopyTo(output, 3);
        return output;
    }

    private static LineEnding Detect(string text)
    {
        var lineFeeds = Count(text, '\n');
        var crlf = CountCrlf(text);
        if (crlf > 0 && crlf * 2 > lineFeeds)
        {
            return LineEnding.Crlf;
        }

        return LineEnding.Lf;
    }

    private static string ToLf(string text)
    {
        if (!text.Contains("\r\n", StringComparison.Ordinal))
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Restore(string text, LineEnding ending)
    {
        if (ending == LineEnding.Lf)
        {
            return text;
        }

        return ToLf(text).Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static int Count(string text, char value)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == value)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountCrlf(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '\r' && text[i + 1] == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
