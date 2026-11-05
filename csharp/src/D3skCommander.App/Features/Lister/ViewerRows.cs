using System.Collections;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.Lister;

/// <summary>Monospace text of a table row: cells padded to the table's width hints.</summary>
internal static class ViewerRowText
{
    private const string Separator = " | ";

    public static string Header(ViewerTable table) => Join(table, table.Columns);

    public static string Row(ViewerTable table, int index) => Join(table, table.Rows[index]);

    private static string Join(ViewerTable table, IReadOnlyList<string> cells)
    {
        var parts = new string[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var width = i < table.Widths.Count ? table.Widths[i] : 0;
            parts[i] = cells[i].PadRight(width);
        }

        return string.Join(Separator, parts);
    }
}

/// <summary>A read-only list whose items are made when asked for, so a huge file costs nothing until scrolled.</summary>
internal sealed class LazyRows : IList, IReadOnlyList<ViewerLine>
{
    private readonly Func<int, ViewerLine> _make;

    public LazyRows(int count, Func<int, ViewerLine> make)
    {
        Count = count;
        _make = make;
    }

    public int Count { get; }

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public ViewerLine this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _make(index);
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public IEnumerator<ViewerLine> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return _make(i);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value)
    {
        for (var i = 0; i < Count; i++)
        {
            if (Equals(_make(i), value))
            {
                return i;
            }
        }

        return -1;
    }

    public void CopyTo(Array array, int index)
    {
        foreach (var row in this)
        {
            array.SetValue(row, index++);
        }
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();
}
