using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using D3skCommander.App.Icons;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    /// <summary>The icon buttons in front of the path box, left to right.</summary>
    public IReadOnlyList<Button> BarButtons => Bar.Children.OfType<Button>().ToList();

    private void BuildBar(PanelPaneModel model)
    {
        Bar.Children.Clear();
        foreach (var item in model.BarButtons)
        {
            var button = new Button { Tag = item.Command };
            button.Classes.Add("bar");
            var glyph = new Avalonia.Controls.Shapes.Path { Data = PanelBarIcons.For(item.Command), Width = 16, Height = 16, Stretch = Stretch.None };
            glyph.Bind(Shape.FillProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
            button.Content = glyph;
            ToolTip.SetTip(button, item.Tip);
            AutomationProperties.SetName(button, item.Label);
            var command = item.Command;
            button.Click += (_, _) => model.RunBar(command);
            Bar.Children.Add(button);
        }
    }
}
