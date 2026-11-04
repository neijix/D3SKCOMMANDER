using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class MenuTests
{
    [Fact]
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
    public void CommandsWithoutAHandlerAreVisibleButDisabled()
    {
        using var host = ShellHost.Open(Language.En);
        var menu = host.Window.FindControl<Menu>("MainMenu");
        Assert.NotNull(menu);
        Assert.Equal(CommandCatalog.Menus.Count, menu.Items.Count);
        for (var i = 0; i < CommandCatalog.Menus.Count; i++)
        {
            AssertTopMenu((MenuItem)menu.Items[i]!, CommandCatalog.Menus[i], host.Localizer);
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

    private static void AssertTopMenu(MenuItem top, MenuDefinition definition, ILocalizer localizer)
    {
        Assert.Equal(localizer.Get(definition.TitleKey), top.Header as string);
        Assert.Equal(definition.Items.Count, top.Items.Count);
        for (var i = 0; i < definition.Items.Count; i++)
        {
            AssertEntry(top.Items[i], definition.Items[i], localizer);
        }
    }

    private static void AssertEntry(object? actual, CommandId? expected, ILocalizer localizer)
    {
        if (expected is null)
        {
            Assert.IsType<Separator>(actual);
            return;
        }

        var item = Assert.IsType<MenuItem>(actual);
        Assert.Equal(localizer.Get(CommandCatalog.LabelKey(expected.Value)), item.Header as string);
        Assert.True(item.IsVisible);
        Assert.Equal(expected == CommandId.Quit, item.IsEnabled);
        AssertGesture(expected.Value, item.InputGesture);
    }

    private static void AssertGesture(CommandId command, object? gesture)
    {
        var primary = CommandCatalog.PrimaryGesture(command);
        if (primary is null)
        {
            Assert.Null(gesture);
            return;
        }

        var keyGesture = Assert.IsType<KeyGesture>(gesture);
        var expected = GestureMap.ToKeyGesture(primary.Value);
        Assert.Equal(expected.Key, keyGesture.Key);
        Assert.Equal(expected.KeyModifiers, keyGesture.KeyModifiers);
    }
}
