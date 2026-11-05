using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.Compare;

namespace D3skCommander.App.Features.Compare;

/// <summary>One side-by-side row: numbers, texts and the tints of its <see cref="RowKind"/>.</summary>
public sealed partial class CompareRow : ObservableObject
{
    private static readonly IBrush Changed = new SolidColorBrush(Color.FromArgb(90, 255, 204, 51));
    private static readonly IBrush Removed = new SolidColorBrush(Color.FromArgb(90, 255, 69, 69));
    private static readonly IBrush Added = new SolidColorBrush(Color.FromArgb(90, 61, 199, 79));
    private static readonly IBrush Gap = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
    private static readonly IBrush Mark = new SolidColorBrush(Color.FromArgb(115, 89, 140, 242));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberBrush))]
    private bool _inBlock;

    public CompareRow(DiffRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row = row;
        LeftNumber = Number(row.LeftNumber);
        RightNumber = Number(row.RightNumber);
        Left = Shown(row.Left);
        Right = Shown(row.Right);
        (LeftBrush, RightBrush) = Tint(row.Kind);
    }

    public DiffRow Row { get; }

    public string LeftNumber { get; }

    public string RightNumber { get; }

    public string Left { get; }

    public string Right { get; }

    public IBrush? LeftBrush { get; }

    public IBrush? RightBrush { get; }

    public IBrush? NumberBrush => InBlock ? Mark : LeftBrush;

    private static (IBrush? Left, IBrush? Right) Tint(RowKind kind) => kind switch
    {
        RowKind.Changed => (Changed, Changed),
        RowKind.LeftOnly => (Removed, Gap),
        RowKind.RightOnly => (Gap, Added),
        _ => (null, null),
    };

    private static string Number(int? value) =>
        value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    private static string Shown(string? text) => text?.Replace("\t", "    ", StringComparison.Ordinal) ?? "";
}
