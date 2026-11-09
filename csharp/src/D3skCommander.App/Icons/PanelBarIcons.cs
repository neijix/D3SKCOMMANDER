using Avalonia.Media;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Icons;

/// <summary>
/// Original one-colour glyphs on a 16-unit grid for the panel bar buttons; the button's
/// foreground fills them, so they follow the light and dark themes.
/// </summary>
public static class PanelBarIcons
{
    private const string House = "M8,1.5 L15,8 h-2 v6 h-3.5 v-4 h-3 v4 h-3.5 v-6 h-2 z";
    private const string Drive = "F0 M1,3.5 h14 v9 h-14z M2.5,5 h11 v3.2 h-11z M10.6,9.6 h2.6 v1.5 h-2.6z";
    private const string Up = "M8,1.5 L14,7.5 L10.2,7.5 L10.2,14.5 L5.8,14.5 L5.8,7.5 L2,7.5 Z";
    private const string Star = "M8,1.1 L9.76,5.97 L14.94,6.14 L10.85,9.33 L12.29,14.31 L8,11.4 L3.71,14.31 L5.15,9.33 L1.06,6.14 L6.24,5.97 Z";
    private const string Clock = "F0 M8,1 A7,7 0 1 1 7.99,1 Z M8,2.5 A5.5,5.5 0 1 0 8.01,2.5 Z M7.3,4 h1.4 v3.7 l2.6,1.6 l-0.7,1.2 l-3.3,-2 Z";
    private const string Network = "M5.5,1 h5 v4 h-5z M7.4,5 h1.2 v2.4 h-1.2z M2.4,7.4 h11.2 v1.2 h-11.2z M2.4,8.6 h1.2 v2.4 h-1.2z M12.4,8.6 h1.2 v2.4 h-1.2z M0.5,11 h5 v4 h-5z M10.5,11 h5 v4 h-5z";
    private const string Transfer = "M4.5,1 L8,5 L5.6,5 L5.6,10 L3.4,10 L3.4,5 L1,5 Z M11.5,15 L15,11 L12.6,11 L12.6,6 L10.4,6 L10.4,11 L8,11 Z";

    private static readonly Dictionary<CommandId, string> Paths = new()
    {
        [CommandId.Computer] = House,
        [CommandId.DrivesLeft] = Drive,
        [CommandId.DrivesRight] = Drive,
        [CommandId.Parent] = Up,
        [CommandId.Hotlist] = Star,
        [CommandId.History] = Clock,
        [CommandId.NetConnect] = Network,
        [CommandId.Ftp] = Transfer,
    };

    /// <summary>The glyph of a panel bar command, or null when it has none.</summary>
    public static Geometry? For(CommandId command) =>
        Paths.TryGetValue(command, out var data) ? Geometry.Parse(data) : null;
}
