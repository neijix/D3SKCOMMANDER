namespace D3skCommander.Core.Panels;

/// <summary>
/// Which detail columns a panel shows. Widths are v0.1's point sizes:
/// name minimum 80, extension 50, size 80, date 130, attributes 90.
/// </summary>
public sealed record Columns(bool Extension, bool Size, bool Date, bool Attributes)
{
    public const float NameMinimum = 80f;

    public const float ExtensionWidth = 50f;

    public const float SizeWidth = 80f;

    public const float DateWidth = 130f;

    public const float AttributesWidth = 90f;

    public static Columns Default { get; } = new(true, true, true, true);

    /// <summary>
    /// Columns that fit in <paramref name="width"/>, counting the name column's minimum and
    /// <paramref name="gap"/> after each extra column. A narrow row drops attributes, then
    /// extension, then date, then size. A column that is already off stays off.
    /// </summary>
    public Columns Fitting(float width, float gap)
    {
        var extension = Extension;
        var size = Size;
        var date = Date;
        var attributes = Attributes;
        if (Used(extension, size, date, attributes, gap) > width)
        {
            attributes = false;
        }

        if (Used(extension, size, date, attributes, gap) > width)
        {
            extension = false;
        }

        if (Used(extension, size, date, attributes, gap) > width)
        {
            date = false;
        }

        if (Used(extension, size, date, attributes, gap) > width)
        {
            size = false;
        }

        return new Columns(extension, size, date, attributes);
    }

    private static float Used(bool extension, bool size, bool date, bool attributes, float gap)
    {
        var total = NameMinimum;
        total += Extra(extension, ExtensionWidth, gap);
        total += Extra(size, SizeWidth, gap);
        total += Extra(date, DateWidth, gap);
        total += Extra(attributes, AttributesWidth, gap);
        return total;
    }

    private static float Extra(bool on, float columnWidth, float gap) => on ? columnWidth + gap : 0f;
}
