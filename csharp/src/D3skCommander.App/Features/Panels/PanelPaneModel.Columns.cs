using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    /// <summary>The names of the column sets, and the one this pane shows.</summary>
    public IReadOnlyList<string> ColumnSetNames => [.. _panels.ColumnSets.Select(set => set.Name)];

    public string CurrentColumnSet => _panels.ColumnSetFor(_side).Name;

    public string ColumnSetsEditText => _text.Get("columnsets.edit");

    /// <summary>The header menu chose a set: this pane's tab shows it.</summary>
    public void UseColumnSet(string name)
    {
        Activate();
        _panels.UseColumnSet(_side, name);
    }

    /// <summary>The header menu chose "Edit column sets…": the shell opens Settings at that section.</summary>
    public void EditColumnSets()
    {
        Activate();
        _dispatch?.Invoke(CommandId.EditColumnSets);
    }

    /// <summary>A sortable header was clicked: this side becomes active and sorts by that column (again: the other way).</summary>
    public void SortBy(ColumnId id)
    {
        var key = id switch
        {
            ColumnId.Name => SortKey.Name,
            ColumnId.Extension => SortKey.Extension,
            ColumnId.Size => SortKey.Size,
            ColumnId.Modified => SortKey.Modified,
            ColumnId.Attributes => SortKey.Attributes,
            ColumnId.Kind => SortKey.Kind,
            _ => (SortKey?)null,
        };
        if (key is { } sort)
        {
            Activate();
            _ = _panels.SortAsync(sort);
        }
    }

    /// <summary>A header grip was released: the width is kept in the set this pane shows.</summary>
    public void SaveWidth(ColumnId id, double width) =>
        _panels.SaveColumnWidth(CurrentColumnSet, id, width);
}
