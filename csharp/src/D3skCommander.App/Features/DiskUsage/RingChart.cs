using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using D3skCommander.Core.DiskUsage;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>
/// The ring chart: the node on show in the middle and its descendants as rings. Drawing only; the layout is
/// <see cref="RingLayout"/> and the hit test <see cref="RingGeometry"/>. Hover tips, a click on a segment
/// and a click on the middle are events for the window to act on.
/// </summary>
public sealed class RingChart : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<RingChart, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> SeparatorProperty =
        AvaloniaProperty.Register<RingChart, IBrush?>(nameof(Separator));

    private const double LabelSize = 10;

    private IReadOnlyList<RingSegment> _segments = [];
    private int[] _branches = [];
    private RingSegment? _hot;
    private bool _centerHot;
    private string _title = "";
    private string _subtitle = "";

    static RingChart()
    {
        AffectsRender<RingChart>(ForegroundProperty, SeparatorProperty);
        FocusableProperty.OverrideDefaultValue<RingChart>(false);
    }

    /// <summary>The text of a segment's tooltip.</summary>
    public Func<RingSegment, string>? Describe { get; set; }

    /// <summary>The short name drawn inside a segment when it fits; null or empty draws none.</summary>
    public Func<RingSegment, string>? Label { get; set; }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Drawn between segments and behind the centre; the window background.</summary>
    public IBrush? Separator
    {
        get => GetValue(SeparatorProperty);
        set => SetValue(SeparatorProperty, value);
    }

    public IReadOnlyList<RingSegment> Segments => _segments;

    public event EventHandler<RingSegment>? SegmentClicked;

    public event EventHandler? CenterClicked;

    /// <summary>The segments to draw and the two lines for the middle.</summary>
    public void Show(IReadOnlyList<RingSegment> segments, string title, string subtitle)
    {
        ArgumentNullException.ThrowIfNull(segments);
        _segments = segments;
        _branches = BranchesOf(segments);
        _title = title;
        _subtitle = subtitle;
        _hot = null;
        _centerHot = false;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    /// <summary>The middle of the chart and the two radii, from the current size.</summary>
    public (Point Center, double InnerRadius, double RingWidth) Metrics()
    {
        var outer = Math.Max(0, (Math.Min(Bounds.Width, Bounds.Height) / 2) - 6);
        var inner = outer * 0.24;
        return (new Point(Bounds.Width / 2, Bounds.Height / 2), inner, (outer - inner) / RingLayout.MaxLevels);
    }

    public RingHit HitAt(Point point)
    {
        var (center, inner, width) = Metrics();
        return RingGeometry.Hit(_segments, point.X - center.X, point.Y - center.Y, inner, width);
    }

    /// <summary>The point in the middle of a segment, for tests and for anything that wants to aim at it.</summary>
    public Point PointOf(RingSegment segment)
    {
        var (center, inner, width) = Metrics();
        var (x, y) = RingGeometry.MiddleOf(segment, inner, width);
        return new Point(center.X + x, center.Y + y);
    }

    /// <summary>What a left click at <paramref name="point"/> does. The pointer handler calls this too.</summary>
    public bool ClickAt(Point point)
    {
        var hit = HitAt(point);
        if (hit.IsCenter)
        {
            CenterClicked?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (hit.Segment is { } segment)
        {
            SegmentClicked?.Invoke(this, segment);
            return true;
        }

        return false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var (center, inner, width) = Metrics();
        if (inner <= 0)
        {
            return;
        }

        var separator = Separator ?? Brushes.Transparent;
        var pen = new Pen(separator, 1.2);
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var color = segment.IsOther ? DiskUsageHeat.Other : DiskUsageHeat.ForBranch(_branches[i], segment.Level);
            var fill = new SolidColorBrush(color, ReferenceEquals(segment, _hot) ? 1 : 0.88);
            context.DrawGeometry(fill, pen, Arc(center, inner + ((segment.Level - 1) * width), width, segment));
        }

        DrawLabels(context, center, inner, width);
        DrawCenter(context, center, inner, separator);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitAt(e.GetPosition(this));
        if (ReferenceEquals(hit.Segment, _hot) && hit.IsCenter == _centerHot)
        {
            return;
        }

        _hot = hit.Segment;
        _centerHot = hit.IsCenter;
        ToolTip.SetTip(this, _hot is { } segment ? Describe?.Invoke(segment) : null);
        Cursor = _hot is { Node.Kind: SizeNodeKind.Folder } || _centerHot ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hot = null;
        _centerHot = false;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && ClickAt(e.GetPosition(this)))
        {
            e.Handled = true;
        }
    }

    private void DrawCenter(DrawingContext context, Point center, double inner, IBrush separator)
    {
        var foreground = Foreground ?? Brushes.Gray;
        context.DrawEllipse(_centerHot ? new SolidColorBrush(DiskUsageHeat.Other, 0.25) : separator, null, center, inner - 2, inner - 2);
        var title = Text(_title, 13, foreground, FontWeight.SemiBold, (inner - 8) * 2);
        var subtitle = Text(_subtitle, 11, foreground, FontWeight.Normal, (inner - 8) * 2);
        var top = center.Y - ((title.Height + subtitle.Height) / 2);
        context.DrawText(title, new Point(center.X - (title.Width / 2), top));
        context.DrawText(subtitle, new Point(center.X - (subtitle.Width / 2), top + title.Height));
    }

    /// <summary>Whether a <paramref name="textWidth"/> by <paramref name="textHeight"/> label fits upright in the segment.</summary>
    internal static bool Fits(RingSegment segment, double inner, double width, double textWidth, double textHeight)
    {
        var middle = inner + ((segment.Level - 0.5) * width);
        var radians = segment.Sweep * Math.PI / 180;

        // Along the ring: the arc, never more than its chord once the arc bends a lot.
        var along = Math.Min(radians * middle, 2 * middle * Math.Sin(Math.Min(radians, Math.PI) / 2)) * 0.85;
        var across = width * 0.85;
        var angle = (segment.StartAngle + (segment.Sweep / 2)) * Math.PI / 180;
        var sin = Math.Abs(Math.Sin(angle));
        var cos = Math.Abs(Math.Cos(angle));

        // The ring runs left-right at twelve and six o'clock, up-down at three and nine.
        var roomX = (cos * along) + (sin * across);
        var roomY = (sin * along) + (cos * across);
        return textWidth <= roomX && textHeight <= roomY;
    }

    // A name goes inside its segment only when the whole of it fits, upright, in the room the arc leaves.
    private void DrawLabels(DrawingContext context, Point center, double inner, double width)
    {
        if (Label is null)
        {
            return;
        }

        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var name = segment.IsOther ? "" : Label(segment);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var color = DiskUsageHeat.ForBranch(_branches[i], segment.Level);
            var text = new FormattedText(
                name,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                LabelSize,
                new SolidColorBrush(DiskUsageHeat.TextOn(color)));
            if (!Fits(segment, inner, width, text.Width, text.Height))
            {
                continue;
            }

            var (x, y) = RingGeometry.MiddleOf(segment, inner, width);
            context.DrawText(text, new Point(center.X + x - (text.Width / 2), center.Y + y - (text.Height / 2)));
        }
    }

    private static FormattedText Text(string text, double size, IBrush brush, FontWeight weight, double maxWidth) =>
        new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush)
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            MaxTextHeight = size * 1.4,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

    // The branch of a segment is the level-1 ancestor it hangs under, found by the sweep it sits inside.
    private static int[] BranchesOf(IReadOnlyList<RingSegment> segments)
    {
        var firsts = segments.Where(segment => segment.Level == 1).ToList();
        var branches = new int[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            var middle = segments[i].StartAngle + (segments[i].Sweep / 2);
            var at = firsts.FindIndex(first => middle >= first.StartAngle && middle <= first.EndAngle);
            branches[i] = Math.Max(0, at);
        }

        return branches;
    }

    private static StreamGeometry Arc(Point center, double inner, double width, RingSegment segment)
    {
        var outer = inner + width;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var pieces = Math.Max(1, (int)Math.Ceiling(segment.Sweep / 120));
        var step = segment.Sweep / pieces;
        ctx.BeginFigure(PointAt(center, outer, segment.StartAngle), true);
        for (var i = 1; i <= pieces; i++)
        {
            ctx.ArcTo(PointAt(center, outer, segment.StartAngle + (i * step)), new Size(outer, outer), 0, false, SweepDirection.Clockwise);
        }

        ctx.LineTo(PointAt(center, inner, segment.EndAngle));
        for (var i = pieces - 1; i >= 0; i--)
        {
            ctx.ArcTo(PointAt(center, inner, segment.StartAngle + (i * step)), new Size(inner, inner), 0, false, SweepDirection.CounterClockwise);
        }

        ctx.EndFigure(true);
        return geometry;
    }

    private static Point PointAt(Point center, double radius, double angle)
    {
        var (x, y) = RingGeometry.PointAt(angle, radius);
        return new Point(center.X + x, center.Y + y);
    }
}
