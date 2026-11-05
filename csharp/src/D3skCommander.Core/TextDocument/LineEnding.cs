namespace D3skCommander.Core.TextDocument;

/// <summary>The line ending a file was loaded with. The buffer itself is LF-only.</summary>
public enum LineEnding
{
    Lf,
    Crlf,
}

/// <summary>Labels shown beside the editor path.</summary>
public static class LineEndings
{
    public static string Label(LineEnding ending)
    {
        if (ending == LineEnding.Crlf)
        {
            return "CRLF";
        }

        if (ending == LineEnding.Lf)
        {
            return "LF";
        }

        throw new ArgumentOutOfRangeException(nameof(ending), ending, "Unknown line ending.");
    }
}
