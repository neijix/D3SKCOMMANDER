namespace D3skCommander.Core.Compare;

/// <summary>One row of the side-by-side view. Line numbers are 1-based.</summary>
public sealed record DiffRow(RowKind Kind, int? LeftNumber, string? Left, int? RightNumber, string? Right);
