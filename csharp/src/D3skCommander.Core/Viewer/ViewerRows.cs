namespace D3skCommander.Core.Viewer;

/// <summary>Decoded lister text. Ranges elsewhere are UTF-16 indexes into <see cref="Text"/>.</summary>
public readonly record struct ViewerDecoded(string Text, ViewerEncoding Encoding);

/// <summary>One line of decoded text, without its terminator. Indexes are exclusive at the end.</summary>
public readonly record struct LineSpan(int Start, int End);

/// <summary>One display row of a wrapped line: a slice of line <see cref="Line"/>.</summary>
public readonly record struct VisualRow(int Line, int Start, int End, bool First);

/// <summary>
/// One row the text view draws. <see cref="Number"/> is the 1-based line number on the first
/// row of a line, and null on a wrap continuation.
/// </summary>
public readonly record struct ViewerTextRow(int Line, int? Number, string Text, bool Current);
