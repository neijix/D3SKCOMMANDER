using Avalonia.Media;

namespace D3skCommander.App.Icons;

/// <summary>
/// One original vector icon on a 16 x 16 grid: a body in the kind colour, an optional lighter shade
/// (folded corner, folder front, title bar) and an optional white glyph on top.
/// </summary>
public sealed class FileKindIcon
{
    public const double Grid = 16;

    public FileKindIcon(string key, Color colour, string body, string? shade, string? glyph)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(body);
        Key = key;
        Colour = colour;
        Body = Geometry.Parse(body);
        Shade = shade is null ? null : Geometry.Parse(shade);
        Glyph = glyph is null ? null : Geometry.Parse(glyph);
    }

    /// <summary>Stable name of the icon, such as <c>folder</c> or <c>image</c>.</summary>
    public string Key { get; }

    public Color Colour { get; }

    public Geometry Body { get; }

    public Geometry? Shade { get; }

    public Geometry? Glyph { get; }
}
