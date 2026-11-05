namespace D3skCommander.Core.Settings;

/// <summary>v0.1 <c>HotlistItem</c>: a name and a folder.</summary>
public sealed class HotlistItem
{
    private string _name = "";
    private string _path = "";

    public HotlistItem()
    {
    }

    public HotlistItem(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name
    {
        get => _name;
        set => _name = value ?? "";
    }

    public string Path
    {
        get => _path;
        set => _path = value ?? "";
    }
}

/// <summary>The hotlist. An empty list is the default.</summary>
public sealed class Hotlist
{
    public const int SchemaVersion = 1;

    private List<HotlistItem> _items = [];

    public List<HotlistItem> Items
    {
        get => _items;
        set => _items = value ?? [];
    }
}
