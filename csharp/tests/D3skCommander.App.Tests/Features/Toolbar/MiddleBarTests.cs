using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Toolbar;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Toolbar;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Toolbar;

public class MiddleBarTests
{
    [AvaloniaFact]
    public void TheMiddleBarSitsBetweenThePanelsWithCopyMoveAndCompareButtons()
    {
        using var host = ShellHost.Open(Language.En);
        var bar = BarOf(host);

        Assert.True(bar.IsVisible);
        Assert.IsType<StackPanel>(bar.Parent);
        Assert.Equal(1, Grid.GetColumn((Control)bar.Parent!));
        var commands = bar.Children.OfType<Button>().Select(button => ((ToolbarItem)button.Tag!).Command).ToList();
        Assert.Equal(
            MiddleBarDefaults.Items.Where(item => item.Kind == ToolbarItemKind.Command).Select(item => item.Command),
            commands);
        Assert.Contains(CommandId.Copy, commands);
        Assert.Contains(CommandId.Move, commands);
        Assert.Contains(CommandId.ComparePanels, commands);
    }

    [AvaloniaFact]
    public void ClickingAButtonOfTheMiddleBarRunsItsCommand()
    {
        var copies = 0;
        using var host = ShellHost.Open(Language.En, services =>
            services.AddSingleton<IShellModule>(new Probe(CommandId.Copy, () => copies++)));
        var button = BarOf(host).Children.OfType<Button>().First(b => ((ToolbarItem)b.Tag!).Command == CommandId.Copy);

        Assert.Equal("Copy (F5)", ToolTip.GetTip(button));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, copies);
    }

    [AvaloniaFact]
    public void ShowMiddleBarHidesTheBarAndSavesTheChoice()
    {
        using var host = ShellHost.Open(Language.En);
        var settings = host.Provider.GetRequiredService<AppSettings>();
        var shell = host.Provider.GetRequiredService<IShellContributions>();
        var bar = BarOf(host);
        try
        {
            Assert.True(shell.Dispatch(CommandId.ShowMiddleBar));

            Assert.False(bar.IsVisible);
            Assert.False(settings.Preferences.Current.ShowMiddleBar);
            Assert.False(((ShellContributions)shell).CheckedState(CommandId.ShowMiddleBar));
        }
        finally
        {
            if (!settings.Preferences.Current.ShowMiddleBar)
            {
                shell.Dispatch(CommandId.ShowMiddleBar);
            }
        }

        Assert.True(bar.IsVisible);
        Assert.True(settings.Preferences.Current.ShowMiddleBar);
    }

    private static MiddleBarView BarOf(ShellHost host) =>
        host.Window.GetVisualDescendants().OfType<MiddleBarView>().Single();

    private sealed class Probe(CommandId command, Action run) : IShellModule
    {
        public void Contribute(IShellContributions shell) => shell.Handle(command, run);
    }
}
