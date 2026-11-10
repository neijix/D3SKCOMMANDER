namespace D3skCommander.Core.FileSystem;

/// <summary>One mounted volume: where it opens, its label, its space and whether it is a network drive.</summary>
public sealed record DriveEntry(string Path, string Label, string Format, ulong FreeBytes, ulong TotalBytes, bool Network = false);
