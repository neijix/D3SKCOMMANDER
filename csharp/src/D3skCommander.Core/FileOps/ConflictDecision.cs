namespace D3skCommander.Core.FileOps;

/// <summary>What <see cref="ConflictPolicy"/> decided for one existing target.</summary>
internal readonly struct ConflictDecision
{
    private ConflictDecision(bool write, bool cancel, string path)
    {
        Write = write;
        Cancel = cancel;
        Path = path;
    }

    public bool Write { get; }

    public bool Cancel { get; }

    public bool Skip => !Write && !Cancel;

    public string Path { get; }

    public static ConflictDecision WriteTo(string path) => new(true, false, path);

    public static ConflictDecision SkipIt() => new(false, false, "");

    public static ConflictDecision CancelIt() => new(false, true, "");
}
