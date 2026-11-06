using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    /// <summary>The drive buttons above the path row, left to right.</summary>
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
        if (current.Count == items.Count && current.Select(b => (string?)b.Tag).SequenceEqual(items.Select(i => i.Path))
            && current.Select(b => b.Classes.Contains("held")).SequenceEqual(items.Select(i => i.Holds)))
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
            var button = new Button { Tag = item.Path, Content = item.Text };
            button.Classes.Add("drive");
            button.Classes.Set("held", item.Holds);
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
}
