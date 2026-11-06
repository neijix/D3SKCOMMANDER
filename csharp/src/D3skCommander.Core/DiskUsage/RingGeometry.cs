namespace D3skCommander.Core.DiskUsage;

/// <summary>What a point on the ring chart lands on.</summary>
public readonly record struct RingHit(bool IsCenter, RingSegment? Segment);

/// <summary>Polar maths for the ring chart: the same convention as <see cref="RingSegment"/> (0 at the top, clockwise).</summary>
public static class RingGeometry
{
    /// <summary>The spot at <paramref name="angle"/> and <paramref name="radius"/>, relative to the centre (y grows downward).</summary>
    public static (double X, double Y) PointAt(double angle, double radius)
    {
        var radians = angle * Math.PI / 180;
        return (radius * Math.Sin(radians), -radius * Math.Cos(radians));
    }

    /// <summary>The middle of a segment's arc, relative to the centre.</summary>
    public static (double X, double Y) MiddleOf(RingSegment segment, double innerRadius, double ringWidth)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return PointAt(segment.StartAngle + (segment.Sweep / 2), innerRadius + ((segment.Level - 0.5) * ringWidth));
    }

    /// <summary>The centre, a segment, or nothing (<c>Segment</c> null and not centre) at the point (<paramref name="dx"/>, <paramref name="dy"/>) from the centre.</summary>
    public static RingHit Hit(IReadOnlyList<RingSegment> segments, double dx, double dy, double innerRadius, double ringWidth)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var radius = Math.Sqrt((dx * dx) + (dy * dy));
        if (radius < innerRadius)
        {
            return new RingHit(true, null);
        }

        if (ringWidth <= 0)
        {
            return default;
        }

        var level = (int)Math.Floor((radius - innerRadius) / ringWidth) + 1;
        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (angle < 0)
        {
            angle += 360;
        }

        foreach (var segment in segments)
        {
            if (segment.Level == level && angle >= segment.StartAngle && angle < segment.EndAngle)
            {
                return new RingHit(false, segment);
            }
        }

        return default;
    }
}
