namespace D3skCommander.Core.Compare;

/// <summary>A half-open range of diff rows. <see cref="End"/> is exclusive.</summary>
public readonly record struct BlockSpan(int Start, int End);
