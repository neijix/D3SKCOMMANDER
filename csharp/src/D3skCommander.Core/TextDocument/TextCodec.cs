using System.Text;
using System.Text.Unicode;

namespace D3skCommander.Core.TextDocument;

/// <summary>Line endings and the UTF-8 round trip of an editor buffer. Offsets elsewhere are UTF-8 bytes.</summary>
public static class TextCodec
{
    /// <summary>CRLF when most newlines are CRLF, otherwise LF (also when there is none).</summary>
    public static LineEnding DetectLineEnding(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lf = 0;
        var crlf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            lf++;
            if (i > 0 && text[i - 1] == '\r')
            {
                crlf++;
            }
        }

        if (crlf > 0 && crlf * 2 > lf)
        {
            return LineEnding.Crlf;
        }

        return LineEnding.Lf;
    }

    /// <summary>The editing-buffer form: every CRLF becomes LF.</summary>
    public static string ToLf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains("\r\n", StringComparison.Ordinal))
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>Puts an LF buffer back into the line ending it was loaded with. A lone CR is not doubled.</summary>
    public static string RestoreLineEnding(string text, LineEnding ending)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (ending == LineEnding.Lf)
        {
            return text;
        }

        if (ending != LineEnding.Crlf)
        {
            throw new ArgumentOutOfRangeException(nameof(ending), ending, "Unknown line ending.");
        }

        return ToLf(text).Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Strips a UTF-8 BOM. Invalid UTF-8 is kept as U+FFFD (<see cref="DecodedText.Lossy"/>);
    /// saving those bytes writes the replacement, not the original bytes.
    /// </summary>
    public static DecodedText Decode(ReadOnlySpan<byte> bytes)
    {
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var body = bom ? bytes[3..] : bytes;
        var raw = Encoding.UTF8.GetString(body);
        return new DecodedText(ToLf(raw), DetectLineEnding(raw), bom, !Utf8.IsValid(body));
    }

    /// <summary>The bytes to write: optional BOM, then the buffer in its original line ending.</summary>
    public static byte[] Encode(string text, LineEnding ending, bool bom)
    {
        var payload = Encoding.UTF8.GetBytes(RestoreLineEnding(text, ending));
        if (!bom)
        {
            return payload;
        }

        var output = new byte[payload.Length + 3];
        output[0] = 0xEF;
        output[1] = 0xBB;
        output[2] = 0xBF;
        payload.CopyTo(output, 3);
        return output;
    }
}
