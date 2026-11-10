using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using D3skCommander.App.Icons;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    /// <summary>The drive and favorite buttons above the path row, left to right.</summary>
    public IReadOnlyList<Button> DriveButtons => DriveStrip.Children.OfType<Button>().ToList();

    private void ShowDrives(PanelPaneModel model)
    {
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelPaneModel.DriveButtons))
            {
                FillDrives(model);
            }
        };
        FillDrives(model);
    }

    private void FillDrives(PanelPaneModel model)
    {
        var items = model.DriveButtons;
        var current = DriveButtons;
        if (current.Count == items.Count && current.Select(b => (DriveButton?)b.DataContext).SequenceEqual(items, SameShape.Instance))
        {
            for (var i = 0; i < items.Count; i++)
            {
                ToolTip.SetTip(current[i], items[i].Tip);
            }

            return;
        }

        DriveStrip.Children.Clear();
        foreach (var item in items)
        {
            var button = new Button { Tag = item.Path, DataContext = item };
            button.Content = ContentOf(item, button);
            button.Classes.Add("drive");
            button.Classes.Set("held", item.Holds);
            button.Classes.Set("favorite", item.Favorite);
            button.Classes.Set("network", item.Network);
            ToolTip.SetTip(button, item.Tip);
            AutomationProperties.SetName(button, item.Text);
            var path = item.Path;
            button.Click += (_, _) => model.GoToDrive(path);
            button.AddHandler(
                PointerReleasedEvent,
                (object? _, PointerReleasedEventArgs e) =>
                {
                    if (e.InitialPressMouseButton == MouseButton.Middle)
                    {
                        model.OpenDriveInNewTab(path);
                        e.Handled = true;
                    }
                },
                RoutingStrategies.Tunnel);
            DriveStrip.Children.Add(button);
        }
    }

    // A favorite shows the gold star before its name; a drive, a disk or network glyph in the button's colour before its letter.
    private static StackPanel ContentOf(DriveButton item, Button button)
    {
        Control icon;
        if (item.Favorite)
        {
            icon = MenuIcons.FavoriteStar();
        }
        else
        {
            var glyph = new Avalonia.Controls.Shapes.Path { Data = PanelBarIcons.DriveGlyph(item.Network), Width = 16, Height = 16, Stretch = Stretch.None };
            glyph.Bind(Shape.FillProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
            icon = glyph;
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new Viewbox { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Child = icon },
                new TextBlock { Text = item.Text, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }

    // Same buttons on screen: the tooltip alone may change in place.
    private sealed class SameShape : IEqualityComparer<DriveButton?>
    {
        public static readonly SameShape Instance = new();

        public bool Equals(DriveButton? x, DriveButton? y) =>
            x is not null && y is not null && x.Path == y.Path && x.Text == y.Text && x.Holds == y.Holds && x.Favorite == y.Favorite && x.Network == y.Network;

        public int GetHashCode(DriveButton? obj) => obj?.Path.GetHashCode(StringComparison.Ordinal) ?? 0;
    }
}
