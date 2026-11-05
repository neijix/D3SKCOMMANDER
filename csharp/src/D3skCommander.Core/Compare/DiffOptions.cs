namespace D3skCommander.Core.Compare;

/// <summary>What the diff ignores when it decides two lines match. The row still shows the original text.</summary>
public readonly record struct DiffOptions(bool IgnoreCase, bool IgnoreWhitespace)
{
    public static DiffOptions Default => new(false, false);
}
