namespace D3skCommander.Core.DiskUsage;

/// <summary>What a scan has seen so far.</summary>
public readonly record struct ScanProgress(long Files, long Folders, long Bytes);
