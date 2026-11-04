using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

public partial class MainWindow
{
    private void BuildChrome()
    {
        StatusLine.Text = _localizer.Get("st.ready");
        AutomationProperties.SetName(StatusLine, _localizer.Get("shell.status"));
        FunctionKeyBar.Child = BuildFunctionKeys();
        FillRegions();
    }

    private Grid BuildFunctionKeys()
    {
        var keys = CommandCatalog.FunctionKeys;
        var grid = new Grid();
        for (var i = 0; i < keys.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            var button = CreateFunctionButton(keys[i]);
            Grid.SetColumn(button, i);
            grid.Children.Add(button);
        }

        return grid;
    }

    private Button CreateFunctionButton(FunctionKey key)
    {
        var button = new Button
        {
            Content = Chord(key.Command) + " " + _localizer.Get(key.LabelKey),
            Tag = key.Command,
            IsEnabled = _shell.CanExecute(key.Command),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(2),
            MinHeight = 28,
        };
        var command = key.Command;
        button.Click += (_, _) => _shell.Dispatch(command);
        return button;
    }

    private static string Chord(CommandId command)
    {
        var gesture = CommandCatalog.PrimaryGesture(command);
        if (gesture is null)
        {
            throw new InvalidOperationException($"Function key {command} has no gesture.");
        }

        return gesture.Value.Format();
    }

    private void FillRegions()
    {
        if (_shell.LeftPanel is not null)
        {
            LeftPanel.Content = RequireControl(_shell.LeftPanel);
        }

        if (_shell.RightPanel is not null)
        {
            RightPanel.Content = RequireControl(_shell.RightPanel);
        }

        AutomationProperties.SetName(LeftPanel, _localizer.Get("shell.panel.left"));
        AutomationProperties.SetName(RightPanel, _localizer.Get("shell.panel.right"));
        foreach (var build in _shell.UnderPanels)
        {
            UnderPanels.Children.Add(RequireControl(build));
        }

        foreach (var build in _shell.BesidePanels)
        {
            BesidePanels.Children.Add(RequireControl(build));
        }
    }

    private static Control RequireControl(Func<Control> build)
    {
        var control = build();
        if (control is null)
        {
            throw new InvalidOperationException("A shell view factory returned null.");
        }

        return control;
    }
}
