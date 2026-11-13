namespace D3skCommander.Core.Settings;

/// <summary>v0.1 <c>HotlistItem</c>: a name and a path; the path may name a folder or a file (probed, never stored).</summary>
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

    /// <summary>Adds <paramref name="item"/>; an item already listed under the same path is replaced.</summary>
    public void Add(HotlistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.RemoveAll(existing => string.Equals(existing.Path, item.Path, StringComparison.Ordinal));
        _items.Add(item);
    }

    /// <summary>A copy whose items can change without touching this list.</summary>
    public Hotlist Clone() => new() { Items = [.. _items.Select(item => new HotlistItem(item.Name, item.Path))] };

    /// <summary>Gives the item at <paramref name="index"/> a new name; a blank name or a bad index changes nothing.</summary>
    public bool Rename(int index, string name)
    {
        if (index < 0 || index >= _items.Count || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        _items[index].Name = name.Trim();
        return true;
    }

    /// <summary>Removes the item at <paramref name="index"/>; a bad index changes nothing.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        _items.RemoveAt(index);
        return true;
    }

    /// <summary>Moves the item at <paramref name="index"/> by <paramref name="delta"/> places; false when it would leave the list.</summary>
    public bool Move(int index, int delta)
    {
        var to = index + delta;
        if (index < 0 || index >= _items.Count || to < 0 || to >= _items.Count || delta == 0)
        {
            return false;
        }

        var item = _items[index];
        _items.RemoveAt(index);
        _items.Insert(to, item);
        return true;
    }

    /// <summary>Same names and paths in the same order.</summary>
    public bool SameItems(Hotlist other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _items.Count == other._items.Count
            && _items.Zip(other._items).All(pair => pair.First.Name == pair.Second.Name && pair.First.Path == pair.Second.Path);
    }
}
