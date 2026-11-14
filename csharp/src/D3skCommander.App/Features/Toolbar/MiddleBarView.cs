using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>The column between the panels: one flat icon button per entry, centred top to bottom.</summary>
public sealed class MiddleBarView : StackPanel
{
    public MiddleBarView(MiddleBarModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Name = "MiddleBar";
        Orientation = Orientation.Vertical;
        Spacing = 2;
        Margin = new Thickness(0, 40, 0, 0);
        VerticalAlignment = VerticalAlignment.Top;
        IsVisible = model.IsVisible;
        foreach (var button in model.Buttons())
        {
            Children.Add(button.IsSeparator ? ToolbarView.Rule(this, Orientation.Vertical) : ToolbarView.Create(button, model.Press));
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MiddleBarModel.IsVisible))
            {
                IsVisible = model.IsVisible;
            }
        };
    }
}

/// <summary>The shell widget of the middle bar.</summary>
public sealed class MiddleBarWidget(MiddleBarModel model) : IShellWidget
{
    public string? Name => "MiddleBar";

    public Control CreateContent() => new MiddleBarView(model);
}
