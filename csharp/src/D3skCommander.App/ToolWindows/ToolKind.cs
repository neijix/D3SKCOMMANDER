namespace D3skCommander.App.ToolWindows;

/// <summary>
/// One tool window. The id is the identity: one window, and one remembered frame, per id.
/// A later feature calls <see cref="Define"/> for a stub, or uses a built-in kind.
/// </summary>
public sealed class ToolKind
{
    // v0.1 egui defaults, in device-independent pixels. The host clamps them onto the screen.
    private ToolKind(string id, string titleKey, int defaultWidth, int defaultHeight)
    {
        Id = id;
        TitleKey = titleKey;
        DefaultWidth = defaultWidth;
        DefaultHeight = defaultHeight;
    }

    public string Id { get; }

    public string TitleKey { get; }

    public int DefaultWidth { get; }

    public int DefaultHeight { get; }

    public static ToolKind Search { get; } = new("search", "tool.search.title", 820, 620);

    public static ToolKind MultiRename { get; } = new("multirename", "tool.multirename.title", 760, 600);

    public static ToolKind Sync { get; } = new("sync", "tool.sync.title", 980, 600);

    public static ToolKind Ftp { get; } = new("ftp", "tool.ftp.title", 940, 580);

    public static ToolKind Settings { get; } = new("settings", "tool.settings.title", 560, 420);

    public static ToolKind Log { get; } = new("log", "tool.log.title", 720, 480);

    public static ToolKind Lister { get; } = new("lister", "tool.lister.title", 900, 650);

    public static ToolKind Editor { get; } = new("editor", "tool.editor.title", 820, 620);

    public static ToolKind Compare { get; } = new("compare", "tool.compare.title", 1000, 650);

    public static ToolKind Passwords { get; } = new("passwords", "tool.passwords.title", 460, 360);

    public static ToolKind Update { get; } = new("update", "tool.update.title", 460, 320);

    public static IReadOnlyList<ToolKind> All { get; } =
    [
        Search,
        MultiRename,
        Sync,
        Ftp,
        Settings,
        Log,
        Lister,
        Editor,
        Compare,
        Passwords,
        Update,
    ];

    public static ToolKind Define(string id, string titleKey, int defaultWidth, int defaultHeight)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(titleKey);
        if (defaultWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultWidth), defaultWidth, "A tool window needs a width.");
        }

        if (defaultHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultHeight), defaultHeight, "A tool window needs a height.");
        }

        return new ToolKind(id, titleKey, defaultWidth, defaultHeight);
    }
}
