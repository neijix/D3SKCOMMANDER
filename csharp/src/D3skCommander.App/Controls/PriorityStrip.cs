using Avalonia;
using Avalonia.Controls;

namespace D3skCommander.App.Controls;

/// <summary>
/// A horizontal row that shows its children left to right while they fit and leaves out the
/// rest (arranged past its clipped right edge, so neither drawn nor hit), keeping <see cref="Reserve"/> pixels free for what follows it.
/// Earlier children win, so put the most useful first.
/// </summary>
public sealed class PriorityStrip : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<PriorityStrip, double>(nameof(Spacing), 2);

    public static readonly StyledProperty<double> ReserveProperty =
        AvaloniaProperty.Register<PriorityStrip, double>(nameof(Reserve));

    static PriorityStrip()
    {
        AffectsMeasure<PriorityStrip>(SpacingProperty, ReserveProperty);
        ClipToBoundsProperty.OverrideDefaultValue<PriorityStrip>(true);
    }

    /// <summary>Gap between two shown children.</summary>
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    /// <summary>Width left free for the sibling that follows the strip.</summary>
    public double Reserve { get => GetValue(ReserveProperty); set => SetValue(ReserveProperty, value); }

    /// <summary>How many children were shown by the last measure.</summary>
    public int ShownCount { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var budget = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width - Reserve;
        double width = 0, height = 0;
        var shown = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var next = width + (shown > 0 ? Spacing : 0) + child.DesiredSize.Width;
            if (next > budget)
            {
                break;
            }
            width = next;
            height = Math.Max(height, child.DesiredSize.Height);
            shown++;
        }
        ShownCount = shown;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (i >= ShownCount)
            {
                child.Arrange(new Rect(finalSize.Width, 0, child.DesiredSize.Width, finalSize.Height));
                continue;
            }
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }
        return finalSize;
    }
}
