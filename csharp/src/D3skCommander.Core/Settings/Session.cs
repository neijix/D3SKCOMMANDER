using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Settings;

/// <summary>v0.1 panel view. Full is the default.</summary>
public enum PanelViewMode
{
    Brief,
    Full,
    Thumbnails,
}

/// <summary>
/// Saved column widths, in the same points as v0.1: name 80, extension 50, size 80, date 130, attributes 90.
/// </summary>
public sealed class ColumnWidths
{
    public float Name { get; set; } = Columns.NameMinimum;

    public float Extension { get; set; } = Columns.ExtensionWidth;

    public float Size { get; set; } = Columns.SizeWidth;

    public float Date { get; set; } = Columns.DateWidth;

    public float Attributes { get; set; } = Columns.AttributesWidth;
}

/// <summary>One tool window's frame. The id is the window's own id; this project does not name them.</summary>
public sealed class ToolWindowGeometry
{
    private string _id = "";

    public string Id
    {
        get => _id;
        set => _id = value ?? "";
    }

    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }
}

/// <summary>One saved panel tab: folder, sort, view, and column widths.</summary>
public sealed class SessionTab
{
    private string _directory = "";
    private ColumnWidths _columnWidths = new();
    private string _columnSet = ColumnSetCatalog.DefaultName;

    public string Directory
    {
        get => _directory;
        set => _directory = value ?? "";
    }

    public SortOrder Sort { get; set; } = SortOrder.NameAscending;

    public PanelViewMode ViewMode { get; set; } = PanelViewMode.Full;

    /// <summary>Name of the column set the tab shows; an unknown name means "Default".</summary>
    public string ColumnSet
    {
        get => _columnSet;
        set => _columnSet = string.IsNullOrWhiteSpace(value) ? ColumnSetCatalog.DefaultName : value;
    }

    public ColumnWidths ColumnWidths
    {
        get => _columnWidths;
        set => _columnWidths = value ?? new ColumnWidths();
    }
}

/// <summary>One panel's tabs and which tab is active. v0.1 <c>PanelConfig</c> starts empty.</summary>
public sealed class SessionPanel
{
    private List<SessionTab> _tabs = [];

    public int ActiveIndex { get; set; }

    public List<SessionTab> Tabs
    {
        get => _tabs;
        set => _tabs = value ?? [];
    }
}

/// <summary>
/// Left and right panels, visited folders, recent net-share addresses, and tool-window frames.
/// History keeps the latest visit first, drops duplicates, and stops at <see cref="HistoryLimit"/>.
/// </summary>
public sealed class Session
{
    public const int SchemaVersion = 1;

    /// <summary>v0.1 <c>push_history</c> cap.</summary>
    public const int HistoryLimit = 30;

    private SessionPanel _left = new();
    private SessionPanel _right = new();
    private List<string> _history = [];
    private List<string> _netShares = [];
    private List<ToolWindowGeometry> _toolWindows = [];

    public SessionPanel Left
    {
        get => _left;
        set => _left = value ?? new SessionPanel();
    }

    public SessionPanel Right
    {
        get => _right;
        set => _right = value ?? new SessionPanel();
    }

    public List<string> History
    {
        get => _history;
        set => _history = value ?? [];
    }

    /// <summary>Network addresses connected before, most recent first. Never a password.</summary>
    public List<string> NetShares
    {
        get => _netShares;
        set => _netShares = value ?? [];
    }

    public List<ToolWindowGeometry> ToolWindows
    {
        get => _toolWindows;
        set => _toolWindows = value ?? [];
    }

    /// <summary>v0.1 <c>Settings::push_history</c>: most recent first, no duplicates, <see cref="HistoryLimit"/> entries.</summary>
    public void RememberDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        History.RemoveAll(path => string.Equals(path, directory, StringComparison.Ordinal));
        History.Insert(0, directory);
        if (History.Count > HistoryLimit)
        {
            History.RemoveRange(HistoryLimit, History.Count - HistoryLimit);
        }
    }
}
