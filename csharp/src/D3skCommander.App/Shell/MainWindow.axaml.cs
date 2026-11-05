using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>The Avalonia plumbing of the main window: the controls drawn from the shell members.</summary>
public sealed partial class MainWindow : Window
{
    private void Build()
    {
        InitializeComponent();
        DataContext = this;
        Title = _localizer.Get("shell.window_title");
        Width = StartSize.Width;
        Height = StartSize.Height;
        foreach (var menu in Menus)
        {
            MainMenu.Items.Add(CreateTopMenu(menu));
        }

        foreach (var widget in MenuBarTrailing)
        {
            TrailingMenu.Items.Add(widget.CreateContent());
        }

        foreach (var button in FunctionKeys)
        {
            FunctionKeyBar.Children.Add(CreateFunctionButton(button));
        }

        Fill(UnderHost, UnderPanels);
        Fill(BesideHost, BesidePanels);
        BesideHost.IsVisible = BesidePanels.Count > 0;
        ShowSlot(LeftSlot, LeftPanel);
        ShowSlot(RightSlot, RightPanel);
        AutomationProperties.SetName(StatusLine, _localizer.Get("shell.status"));
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        HandleOpened();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        CloseOwnedWindows();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ToolWindowOwners.Closed(this);
    }

    private MenuItem CreateTopMenu(ShellMenu menu)
    {
        var top = new MenuItem { Header = menu.Header };
        foreach (var entry in menu.Items)
        {
            top.Items.Add(CreateEntry(entry));
        }

        return top;
    }

    private Control CreateEntry(ShellMenuEntry entry)
    {
        if (entry.IsSeparator)
        {
            return new Separator();
        }

        var item = new MenuItem
        {
            Header = entry.Header,
            IsEnabled = entry.IsEnabled,
            IsVisible = entry.IsVisible,
            Tag = entry.Command,
        };
        if (entry.Gesture is Gesture gesture)
        {
            item.Header = entry.Header + "    " + gesture.Format();
        }

        if (entry.Command is CommandId command)
        {
            item.Click += (_, _) => _shell.Dispatch(command);
        }

        return item;
    }

    private static Button CreateFunctionButton(FunctionButton key)
    {
        var button = new Button
        {
            Content = key.Content,
            Tag = key.Command,
            IsEnabled = key.IsEnabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(2),
            MinHeight = 28,
        };
        button.Click += (_, _) => key.Click();
        return button;
    }

    private static void Fill(Panel host, IReadOnlyList<IShellWidget> widgets)
    {
        foreach (var widget in widgets)
        {
            host.Children.Add(widget.CreateContent());
        }
    }

    private static void ShowSlot(ContentControl host, ShellSlot slot)
    {
        host.Content = slot.Content?.CreateContent() ?? new Border { Name = slot.Name };
        AutomationProperties.SetName(host, slot.Name);
    }
}
