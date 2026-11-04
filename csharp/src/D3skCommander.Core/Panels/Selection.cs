namespace D3skCommander.Core.Panels;

/// <summary>Paths marked in a tab. The tab never marks the ".." row.</summary>
public sealed class Selection
{
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);

    public int Count => _paths.Count;

    public bool Contains(string path) => _paths.Contains(path);

    public void Clear() => _paths.Clear();

    public void Add(string path) => _paths.Add(path);

    public void Remove(string path) => _paths.Remove(path);

    public void Toggle(string path)
    {
        if (!_paths.Remove(path))
        {
            _paths.Add(path);
        }
    }

    public void Retain(IEnumerable<string> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        var keep = new HashSet<string>(present, StringComparer.Ordinal);
        _paths.RemoveWhere(path => !keep.Contains(path));
    }
}
