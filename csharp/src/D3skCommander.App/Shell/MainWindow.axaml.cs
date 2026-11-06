using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && GestureMap.From(e.Key, e.KeyModifiers) is Gesture gesture)
        {
            e.Handled = _model.KeyPress(gesture.Key, gesture.Modifiers);
        }
    }

    /// <summary>True while a quit waits for the owned windows' answers.</summary>
    internal bool IsQuitting => _quitting;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_quitApproved)
        {
            if (_quitting)
            {
                e.Cancel = true;
            }
            else
            {
                _quitting = true;
                var ask = CloseOwnedWindowsAsync();
                if (ask.IsCompleted)
                {
                    _quitting = false;
                    _quitApproved = ask.Result;
                    e.Cancel = !ask.Result;
                }
                else
                {
                    e.Cancel = true;
                    _ = QuitWhenApproved(ask);
                }
            }
        }

        base.OnClosing(e);
    }

    private async Task QuitWhenApproved(Task<bool> ask)
    {
        var approved = await ask.ConfigureAwait(true);
        _quitting = false;
        if (approved)
        {
            _quitApproved = true;
            Close();
        }
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

        if (menu.DynamicItems.Count > 0)
        {
            var fixedCount = top.Items.Count;
            FillDynamic(top, fixedCount, menu.DynamicItems);
            top.SubmenuOpened += (_, _) => FillDynamic(top, fixedCount, menu.DynamicItems);
        }

        return top;
    }

    /// <summary>Replaces the items after the fixed ones with what the sources give now: a rule, then one item each.</summary>
    private static void FillDynamic(MenuItem top, int fixedCount, IReadOnlyList<Func<IReadOnlyList<ShellAction>>> sources)
    {
        while (top.Items.Count > fixedCount)
        {
            top.Items.RemoveAt(top.Items.Count - 1);
        }

        foreach (var source in sources)
        {
            var actions = source();
            if (actions.Count > 0)
            {
                top.Items.Add(new Separator());
            }

            foreach (var action in actions)
            {
                var item = new MenuItem { Header = action.Header, Tag = action };
                if (action.Checked)
                {
                    item.ToggleType = MenuItemToggleType.CheckBox;
                    item.IsChecked = true;
                }

                item.Click += (_, _) => action.Invoke();
                top.Items.Add(item);
            }
        }
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
