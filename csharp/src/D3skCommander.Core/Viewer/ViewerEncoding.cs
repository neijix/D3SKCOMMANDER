namespace D3skCommander.Core.Viewer;

/// <summary>Encoding announced by a byte order mark. No BOM means UTF-8.</summary>
public enum ViewerEncoding
{
    Utf8,
    Utf8Bom,
    Utf16Le,
    Utf16Be,
}

/// <summary>The encoding and how many leading bytes the BOM occupies.</summary>
public readonly record struct ViewerEncodingMark(ViewerEncoding Encoding, int BomLength);

/// <summary>v0.1 <c>Encoding</c> display labels.</summary>
public static class ViewerEncodingNames
{
    public static string Name(ViewerEncoding encoding)
    {
        if (encoding == ViewerEncoding.Utf8Bom)
        {
            return "UTF-8 BOM";
        }

        if (encoding == ViewerEncoding.Utf16Le)
        {
            return "UTF-16 LE";
        }

        if (encoding == ViewerEncoding.Utf16Be)
        {
            return "UTF-16 BE";
        }

        return "UTF-8";
    }
}
