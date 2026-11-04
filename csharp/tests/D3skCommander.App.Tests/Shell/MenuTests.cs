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

    [Fact]
    public void CommandsWithoutAHandlerAreVisibleButDisabled()
    {
        using var host = ShellHost.Open(Language.En);
        var menus = host.Window.Menus;
        Assert.Equal(CommandCatalog.Menus.Count, menus.Count);
        for (var i = 0; i < CommandCatalog.Menus.Count; i++)
        {
            AssertTopMenu(menus[i], CommandCatalog.Menus[i], host.Localizer);
        }
    }

    [Fact]
    public void FunctionKeyChordsRoundTripThroughImGui()
    {
        foreach (var key in CommandCatalog.FunctionKeys)
        {
            var gesture = CommandCatalog.PrimaryGesture(key.Command);
            Assert.NotNull(gesture);
            var imgui = GestureMap.ToImGui(gesture.Value.Key);
            var back = GestureMap.From(imgui, gesture.Value.Modifiers);
            Assert.Equal(gesture, back);
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
        Assert.Equal(expected == CommandId.Quit, actual.IsEnabled);
        Assert.Equal(CommandCatalog.PrimaryGesture(expected.Value), actual.Gesture);
    }
}
