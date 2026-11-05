using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Help;

/// <summary>The shortcut list as a modal window over <see cref="KeysModel"/>.</summary>
public partial class KeysWindow : Window
{
    public KeysWindow()
    {
        InitializeComponent();
    }

    public KeysWindow(KeysModel model, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        InitializeComponent();
        DataContext = model;
        Title = text.Get("cmd.keys");
        FilterBox.PlaceholderText = text.Get("help.keys.filter");
        CommandHeader.Text = text.Get("help.keys.command");
        ShortcutHeader.Text = text.Get("help.keys.shortcut");
        CloseButton.Content = text.Get("close");
        CloseButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => FilterBox.Focus();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
        }
    }
}
