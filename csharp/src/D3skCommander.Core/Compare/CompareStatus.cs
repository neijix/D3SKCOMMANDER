namespace D3skCommander.Core.Compare;

/// <summary>Why the compare session last changed its status line. The view translates <see cref="Kind"/>.</summary>
public enum CompareStatusKind
{
    None,
    Saved,
    TooLarge,
    Error,
}

/// <summary>
/// A status the session recorded. <see cref="Detail"/> is an OS or I/O message for
/// <see cref="CompareStatusKind.Error"/>; <see cref="Path"/> is the file for a save or a size refusal.
/// </summary>
public readonly record struct CompareStatus(CompareStatusKind Kind, string Path, string Detail)
{
    public static CompareStatus Empty => new(CompareStatusKind.None, "", "");

    public static CompareStatus Saved(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new CompareStatus(CompareStatusKind.Saved, path, "");
    }

    public static CompareStatus TooLarge(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new CompareStatus(CompareStatusKind.TooLarge, path, "");
    }

    public static CompareStatus Error(string detail)
    {
        ArgumentException.ThrowIfNullOrEmpty(detail);
        return new CompareStatus(CompareStatusKind.Error, "", detail);
    }
}
