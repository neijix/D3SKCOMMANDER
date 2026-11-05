namespace D3skCommander.Core.FileSystem;

/// <summary>One mounted volume: where it opens, its label and its space.</summary>
public sealed record DriveEntry(string Path, string Label, string Format, ulong FreeBytes, ulong TotalBytes);
