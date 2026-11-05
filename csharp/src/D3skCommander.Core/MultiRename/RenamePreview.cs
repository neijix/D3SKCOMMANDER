namespace D3skCommander.Core.MultiRename;

/// <summary>One file's new path. <see cref="Error"/> blocks apply when set.</summary>
public sealed class RenamePreview
{
    public RenamePreview(string from, string to, string? error)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
        Error = error;
    }

    public string From { get; }

    public string To { get; }

    public string? Error { get; internal set; }
}
