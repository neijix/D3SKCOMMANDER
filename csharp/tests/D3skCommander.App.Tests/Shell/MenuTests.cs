using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class MenuTests
{
    [AvaloniaFact]
    public void EveryCommandIsInAMenuOrBound()
    {
        Assert.Equal(MenuExpectations.Titles, CommandCatalog.Menus.Select(menu => menu.TitleKey).ToArray());
        foreach (var title in MenuExpectations.Titles)
        {
            var menu = CommandCatalog.Menus.Single(item => item.TitleKey == title);
            Assert.Equal(MenuExpectations.Items(title), menu.Items);
        }

        AssertAddOnsAreInAMenu();
        AssertKeyOnlyStayOutOfMenus();
        AssertEveryCommandIsReachable();
    }

    [AvaloniaFact]
    public void CommandsAreEnabledOnlyWhenAHandlerIsRegistered()
    {
        using var host = ShellHost.Open(Language.En);
        var menus = host.Window.Menus;
        Assert.Equal(CommandCatalog.Menus.Count, menus.Count);
        for (var i = 0; i < CommandCatalog.Menus.Count; i++)
        {
            AssertTopMenu(menus[i], CommandCatalog.Menus[i], host.Localizer);
        }
    }

    [AvaloniaFact]
    public void FunctionKeyChordsRoundTripThroughAvaloniaKeys()
    {
        foreach (var key in CommandCatalog.FunctionKeys)
        {
            var gesture = CommandCatalog.PrimaryGesture(key.Command);
            Assert.NotNull(gesture);
            var avalonia = GestureMap.ToKeyGesture(gesture.Value);
            var back = GestureMap.From(avalonia.Key, avalonia.KeyModifiers);
            Assert.Equal(gesture, back);
        }
    }

    [AvaloniaFact]
    public void TopMenusKeepTheCatalogOrderAndLocalizedTitles()
    {
        foreach (var language in new[] { Language.En, Language.Fr })
        {
            using var host = ShellHost.Open(language);
            var titles = TopItems(host.Window).Select(item => (string?)item.Header).ToArray();
            Assert.Equal(
                CommandCatalog.Menus.Select(menu => host.Localizer.Get(menu.TitleKey)).ToArray(),
                titles);
        }
    }

    [AvaloniaFact]
    public void SeparatorsStayAndAModuleItemIsAppendedInOrder()
    {
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IShellModule>(new AppendProbe()));
        var toolsIndex = CommandCatalog.Menus.ToList().FindIndex(menu => menu.TitleKey == "menu.tools");
        var tools = CommandCatalog.Menus[toolsIndex];
        var items = TopItems(host.Window)[toolsIndex].Items.Cast<Control>().ToArray();
        Assert.Equal(tools.Items.Count + 2, items.Length);
        for (var i = 0; i < tools.Items.Count; i++)
        {
            Assert.Equal(tools.Items[i] is null, items[i] is Separator);
        }

        Assert.IsType<Separator>(items[^2]);
        var appended = Assert.IsType<MenuItem>(items[^1]);
        Assert.Equal(CommandId.Copy, appended.Tag);
        Assert.StartsWith(AppendedHeader(host), Assert.IsType<string>(appended.Header), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void EachItemShowsItsPrimaryGestureText()
    {
        using var host = ShellHost.Open(Language.En);
        foreach (var top in TopItems(host.Window))
        {
            foreach (var item in top.Items.OfType<MenuItem>())
            {
                var command = Assert.IsType<CommandId>(item.Tag);
                var gesture = CommandCatalog.PrimaryGesture(command);
                var header = Assert.IsType<string>(item.Header);
                if (gesture is not null)
                {
                    Assert.EndsWith(gesture.Value.Format(), header, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Equal(host.Localizer.Get(CommandCatalog.LabelKey(command)), header);
                }
            }
        }
    }

    [AvaloniaFact]
    public void AMenuBarTrailingItemIsShown()
    {
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IShellModule>(new AppendProbe()));
        var trailing = host.Window.FindControl<Menu>("TrailingMenu");
        Assert.NotNull(trailing);
        var item = Assert.IsType<MenuItem>(Assert.Single(trailing.Items, entry => entry is MenuItem menu && Equals(menu.Header, "probe")));
        Assert.True(item.IsVisible);
    }

    private static string AppendedHeader(ShellHost host) =>
        host.Localizer.Get(CommandCatalog.LabelKey(CommandId.Copy));

    private static MenuItem[] TopItems(Avalonia.Controls.Window window) =>
        window.FindControl<Menu>("MainMenu")!.Items.Cast<MenuItem>().ToArray();

    private sealed class AppendProbe : IShellModule
    {
        public void Contribute(IShellContributions shell)
        {
            shell.Handle(CommandId.Copy, () => { });
            shell.AppendMenuSeparator("menu.tools");
            shell.AppendMenuItem("menu.tools", CommandId.Copy);
            shell.AddMenuBarTrailing(() => new MenuExtraWidget("ProbeExtra", "probe"));
        }
    }

    private static void AssertAddOnsAreInAMenu()
    {
        foreach (var command in MenuExpectations.AddOns())
        {
            Assert.True(InMenu(command), $"{command} is not in a menu");
        }
    }

    private static void AssertKeyOnlyStayOutOfMenus()
    {
        foreach (var command in new[] { CommandId.SwitchPanel, CommandId.DrivesLeft, CommandId.DrivesRight })
        {
            Assert.False(InMenu(command), $"{command} is key-only");
            Assert.Contains(CommandCatalog.Bindings, binding => binding.Command == command);
        }
    }

    private static void AssertEveryCommandIsReachable()
    {
        var bound = CommandCatalog.Bindings.Select(binding => binding.Command).ToHashSet();
        foreach (var command in CommandCatalog.All)
        {
            Assert.True(InMenu(command) || bound.Contains(command), $"{command} is neither in a menu nor bound");
        }
    }

    private static bool InMenu(CommandId command)
    {
        foreach (var menu in CommandCatalog.Menus)
        {
            foreach (var item in menu.Items)
            {
                if (item == command)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AssertTopMenu(ShellMenu top, MenuDefinition definition, ILocalizer localizer)
    {
        Assert.Equal(localizer.Get(definition.TitleKey), top.Header);
        Assert.Equal(definition.Items.Count, top.Items.Count);
        for (var i = 0; i < definition.Items.Count; i++)
        {
            AssertEntry(top.Items[i], definition.Items[i], localizer);
        }
    }

    private static void AssertEntry(ShellMenuEntry actual, CommandId? expected, ILocalizer localizer)
    {
        if (expected is null)
        {
            Assert.True(actual.IsSeparator);
            return;
        }

        Assert.False(actual.IsSeparator);
        Assert.Equal(localizer.Get(CommandCatalog.LabelKey(expected.Value)), actual.Header);
        Assert.True(actual.IsVisible);
        Assert.Equal(HandledCommands.Handles(expected.Value), actual.IsEnabled);
        Assert.Equal(CommandCatalog.PrimaryGesture(expected.Value), actual.Gesture);
    }
}
