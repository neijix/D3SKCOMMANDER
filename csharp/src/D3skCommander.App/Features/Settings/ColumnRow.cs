using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Settings;

/// <summary>One optional column in the column-set editor: shown or not, and its width (0 = automatic).</summary>
public sealed partial class ColumnRow : ObservableObject
{
    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private decimal? _width;

    public ColumnRow(ColumnId id, string label, bool isShown, double width)
    {
        Id = id;
        Label = label;
        _isShown = isShown;
        _width = (decimal)width;
    }

    public ColumnId Id { get; }

    public string Label { get; }
}
