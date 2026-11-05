namespace D3skCommander.Core.TextDocument;

/// <summary>A file after it has been loaded for editing. <see cref="Text"/> is LF-only.</summary>
public sealed record DecodedText(string Text, LineEnding LineEnding, bool Bom, bool Lossy);
