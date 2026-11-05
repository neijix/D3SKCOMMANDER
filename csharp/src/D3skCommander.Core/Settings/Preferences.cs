using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Settings;

/// <summary>
/// v0.1 <c>Settings</c> minus the pieces that moved to the session, the hotlist, and the FTP sites.
/// Defaults match <c>Settings::default</c>: dark mode, 14 pt, trash and delete confirmation on,
/// update check on, and every column visible.
/// </summary>
public sealed class Preferences
{
    public const int SchemaVersion = 1;

    /// <summary>v0.1 <c>font_size</c>.</summary>
    public const float DefaultFontSize = 14f;

    private string _editor = "";
    private string _terminal = "";
    private Columns _columns = Columns.Default;
    private List<UserCommand> _userCommands = [];

    /// <summary>v0.1 <c>Lang::from_env</c>: the saved language, otherwise the system language.</summary>
    public Language Language { get; set; } = Languages.Detect();

    public bool DarkMode { get; set; } = true;

    public float FontSize { get; set; } = DefaultFontSize;

    public bool ShowHidden { get; set; }

    public bool UseTrash { get; set; } = true;

    public bool ConfirmDelete { get; set; } = true;

    public bool LogOperations { get; set; }

    /// <summary>External editor command. Empty uses the built-in editor.</summary>
    public string Editor
    {
        get => _editor;
        set => _editor = value ?? "";
    }

    /// <summary>Terminal command for the current folder. Empty uses the platform default.</summary>
    public string Terminal
    {
        get => _terminal;
        set => _terminal = value ?? "";
    }

    /// <summary>Which detail columns are shown. Widths live on the session.</summary>
    public Columns Columns
    {
        get => _columns;
        set => _columns = value ?? Columns.Default;
    }

    public bool CheckUpdates { get; set; } = true;

    public List<UserCommand> UserCommands
    {
        get => _userCommands;
        set => _userCommands = value ?? [];
    }
}
