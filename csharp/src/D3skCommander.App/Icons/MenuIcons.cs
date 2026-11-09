using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using D3skCommander.Core.Commands;
using Path = Avalonia.Controls.Shapes.Path;

namespace D3skCommander.App.Icons;

/// <summary>
/// Original 16-unit glyphs shown left of a context-menu line: a console window with a prompt for the terminals, the
/// same window in blue for PowerShell, a shield in the corner for the administrator lines and a gold star for Add to
/// favorites. No vendor logo.
/// </summary>
public static class MenuIcons
{
    private const string Window = "F0 M1,2.5 h14 v11 h-14z M2.2,4.8 h11.6 v7.5 h-11.6z";
    private const string Prompt = "M3.6,6.2 L4.5,5.3 L7.3,8.1 L4.5,10.9 L3.6,10 L5.5,8.1 Z M7.8,10.2 h4 v1.1 h-4z";
    private const string Star = "M8,1.2 L10.1,5.6 L14.9,6.2 L11.4,9.5 L12.3,14.3 L8,12 L3.7,14.3 L4.6,9.5 L1.1,6.2 L5.9,5.6 Z";
    private const string Shield = "M12.2,8.6 L15.5,9.6 v2.4 c0,1.8 -1.4,3 -3.3,3.6 c-1.9,-0.6 -3.3,-1.8 -3.3,-3.6 v-2.4 z";

    private static readonly IBrush ShellBlue = new SolidColorBrush(Color.FromRgb(40, 110, 200));
    private static readonly IBrush ShieldGold = new SolidColorBrush(Color.FromRgb(230, 170, 30));

    /// <summary>The icon of a menu line, or null when its command has none.</summary>
    public static Control? For(CommandId? command) => command switch
    {
        CommandId.TerminalHere => Make(blue: false, admin: false),
        CommandId.TerminalAdmin => Make(blue: false, admin: true),
        CommandId.PowerShellHere => Make(blue: true, admin: false),
        CommandId.PowerShellAdmin => Make(blue: true, admin: true),
        CommandId.AddToFavorites => new Panel { Width = 16, Height = 16, Children = { Glyph(Star, ShieldGold) } },
        _ => null,
    };

    private static Panel Make(bool blue, bool admin)
    {
        var icon = new Panel { Width = 16, Height = 16 };
        if (blue)
        {
            icon.Children.Add(new Border { Background = ShellBlue, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(1, 2.5, 1, 2.5) });
            icon.Children.Add(Glyph(Prompt, Brushes.White));
        }
        else
        {
            icon.Children.Add(Glyph(Window, null));
            icon.Children.Add(Glyph(Prompt, null));
        }

        if (admin)
        {
            icon.Children.Add(Glyph(Shield, ShieldGold));
        }

        return icon;
    }

    // A null fill follows the menu's text colour, so the glyph reads in light and dark themes.
    private static Path Glyph(string data, IBrush? fill)
    {
        var path = new Path { Data = Geometry.Parse(data), Width = 16, Height = 16, Stretch = Stretch.None };
        if (fill is not null)
        {
            path.Fill = fill;
        }
        else
        {
            path.Bind(Shape.FillProperty, path.GetObservable(TextElement.ForegroundProperty));
        }

        return path;
    }
}
