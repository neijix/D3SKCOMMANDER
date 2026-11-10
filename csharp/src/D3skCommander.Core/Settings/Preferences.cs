using D3skCommander.Core.Associations;
using D3skCommander.Core.Index;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Toolbar;

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

    public const int DefaultIndexRefreshHours = 24;

    public const int MaxIndexRefreshHours = 24 * 365;

    public const int MaxPreviewStartSeconds = 24 * 3600;

    public const double DefaultTreeWidth = 220;

    public const double MinTreeWidth = 80;

    public const double MaxTreeWidth = 1200;

    private double _treeWidthLeft = DefaultTreeWidth;
    private double _treeWidthRight = DefaultTreeWidth;
    private string _indexRoots = "";
    private string _indexExcludes = IndexOptions.DefaultExcludes;
    private int _indexRefreshHours = DefaultIndexRefreshHours;
    private int _previewStartSeconds;
    private string _editor = "";
    private string _terminal = "";
    private Columns _columns = Columns.Default;
    private List<ColumnSet> _columnSets = [];
    private List<UserCommand> _userCommands = [];
    private List<ToolbarItem> _toolbar = ToolbarDefaults.Items();
    private List<AssociationRule> _associations = [];

    /// <summary>v0.1 <c>Lang::from_env</c>: the saved language, otherwise the system language.</summary>
    public Language Language { get; set; } = Languages.Detect();

    public bool DarkMode { get; set; } = true;

    public float FontSize { get; set; } = DefaultFontSize;

    public bool ShowHidden { get; set; }

    /// <summary>The row of drive buttons above each panel.</summary>
    public bool ShowDriveBar { get; set; } = true;

    /// <summary>The main toolbar between the menu bar and the panels.</summary>
    public bool ShowToolbar { get; set; } = true;

    /// <summary>What choosing a favorite set of tabs does with the open tabs.</summary>
    public FavoriteRestoreMode FavoriteTabsMode { get; set; } = FavoriteRestoreMode.Replace;

    /// <summary>The folder tree beside the left panel's file list.</summary>
    public bool ShowTreeLeft { get; set; }

    /// <summary>The folder tree beside the right panel's file list.</summary>
    public bool ShowTreeRight { get; set; }

    /// <summary>Width of the left folder tree in device-independent pixels, kept between <see cref="MinTreeWidth"/> and <see cref="MaxTreeWidth"/>.</summary>
    public double TreeWidthLeft
    {
        get => _treeWidthLeft;
        set => _treeWidthLeft = ClampTree(value);
    }

    /// <summary>Width of the right folder tree in device-independent pixels.</summary>
    public double TreeWidthRight
    {
        get => _treeWidthRight;
        set => _treeWidthRight = ClampTree(value);
    }

    /// <summary>The toolbar buttons and separators, in order. Starts as <see cref="ToolbarDefaults"/>; an empty list is a toolbar with no button.</summary>
    public List<ToolbarItem> Toolbar
    {
        get => _toolbar;
        set => _toolbar = value is null ? ToolbarDefaults.Items() : ToolbarItems.Clean(value);
    }

    /// <summary>The file associations, first match wins. Empty: Enter, F3 and F4 keep their built-in behavior.</summary>
    public List<AssociationRule> Associations
    {
        get => _associations;
        set => _associations = value is null ? [] : [.. value.Where(rule => rule is not null)];
    }

    public bool UseTrash { get; set; } = true;

    public bool ConfirmDelete { get; set; } = true;

    /// <summary>Ask before deleting permanently when the trash is unavailable ("don't ask again" turns it off).</summary>
    public bool ConfirmTrashFallback { get; set; } = true;

    public bool LogOperations { get; set; }

    /// <summary>Measure the folders of a listing in the background so the size column fills in (off by default: it reads the disk).</summary>
    public bool AutoFolderSizes { get; set; }

    /// <summary>Videos and sounds in the preview start this many seconds in (0: from the start; a shorter file plays from its start).</summary>
    public int PreviewStartSeconds
    {
        get => _previewStartSeconds;
        set => _previewStartSeconds = Math.Clamp(value, 0, MaxPreviewStartSeconds);
    }

    /// <summary>Folders the quick-find index covers, separated by ';'. Empty means every mounted volume.</summary>
    public string IndexRoots
    {
        get => _indexRoots;
        set => _indexRoots = value ?? "";
    }

    /// <summary>Folders the quick-find index leaves out (names, masks or absolute paths), separated by ';'.</summary>
    public string IndexExcludes
    {
        get => _indexExcludes;
        set => _indexExcludes = value ?? "";
    }

    /// <summary>Rebuild the index at start when it is older than this many hours; 0 never refreshes it.</summary>
    public int IndexRefreshHours
    {
        get => _indexRefreshHours;
        set => _indexRefreshHours = Math.Clamp(value, 0, MaxIndexRefreshHours);
    }

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

    /// <summary>
    /// v0.1's four column flags. Kept so old files still load: they make the "Default" set until
    /// <see cref="ColumnSets"/> stores one.
    /// </summary>
    public Columns Columns
    {
        get => _columns;
        set => _columns = value ?? Columns.Default;
    }

    /// <summary>The column sets the user changed or added. Read through <see cref="AllColumnSets"/>.</summary>
    public List<ColumnSet> ColumnSets
    {
        get => _columnSets;
        set => _columnSets = value ?? [];
    }

    /// <summary>"Default", "Details", then the user's sets; "Default" is built from <see cref="Columns"/> when not stored.</summary>
    public IReadOnlyList<ColumnSet> AllColumnSets() => ColumnSetCatalog.Effective(_columnSets, _columns);

    public bool CheckUpdates { get; set; } = true;

    private static double ClampTree(double value) =>
        double.IsNaN(value) ? DefaultTreeWidth : Math.Clamp(value, MinTreeWidth, MaxTreeWidth);

    public List<UserCommand> UserCommands
    {
        get => _userCommands;
        set => _userCommands = value ?? [];
    }
}
