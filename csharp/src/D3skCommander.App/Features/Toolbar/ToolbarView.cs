using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using D3skCommander.App.Icons;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>The toolbar row: one flat icon button per entry, a thin rule per separator, a context menu to customize.</summary>
public sealed class ToolbarView : Border
{
    private const double ButtonSize = 30;
    private const string RuleBrush = "SystemControlForegroundBaseLowBrush";
    private readonly ToolbarModel _model;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };

    public ToolbarView(ToolbarModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        Name = "MainToolbar";
        Padding = new Thickness(4, 2);
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Bind(BorderBrushProperty, this.GetResourceObservable(RuleBrush));
        Child = _row;
        var customize = new MenuItem { Header = model.CustomizeCaption, Name = "CustomizeToolbarItem" };
        customize.Click += (_, _) => model.Customize();
        ContextMenu = new ContextMenu { Items = { customize } };
        IsVisible = model.IsVisible;
        model.Refresh();
        model.ButtonsChanged += Fill;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolbarModel.IsVisible))
            {
                IsVisible = model.IsVisible;
            }
        };
        Fill();
    }

    private void Fill()
    {
        _row.Children.Clear();
        foreach (var button in _model.Buttons)
        {
            _row.Children.Add(button.IsSeparator ? Rule() : Create(button));
        }
    }

    private Border Rule()
    {
        var rule = new Border { Width = 1, Height = 20, Margin = new Thickness(5, 0), VerticalAlignment = VerticalAlignment.Center };
        rule.Bind(BackgroundProperty, this.GetResourceObservable(RuleBrush));
        return rule;
    }

    private Button Create(ToolbarButton button)
    {
        var icon = new PathIcon { Data = ToolbarGlyphs.For(button.Icon), Width = 16, Height = 16 };
        var control = new Button
        {
            Content = icon,
            Width = ButtonSize,
            Height = ButtonSize,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = button.IsEnabled,
            Tag = button.Item,
        };
        ToolTip.SetTip(control, button.Tooltip);
        AutomationProperties.SetName(control, button.Tooltip);
        control.Click += (_, _) => _model.Press(button);
        return control;
    }
}
