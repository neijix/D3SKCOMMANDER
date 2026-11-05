using Avalonia.Headless.XUnit;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class FunctionKeyBarTests
{
    [AvaloniaFact]
    public void ShowsTheEightFunctionKeys()
    {
        using var host = ShellHost.Open(Language.En);
        var buttons = host.Window.FunctionKeys;
        var expected = new[]
        {
            "F3 View",
            "F4 Edit",
            "F5 Copy",
            "F6 Move",
            "F7 NewFolder",
            "F8 Delete",
            "Alt+F7 Search",
            "Alt+F4 Exit",
        };
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], buttons[i].Content);
        }

        Assert.Equal(
            [
                CommandId.View,
                CommandId.Edit,
                CommandId.Copy,
                CommandId.Move,
                CommandId.MkDir,
                CommandId.Delete,
                CommandId.Search,
                CommandId.Quit,
            ],
            buttons.Select(button => button.Command).ToArray());
        foreach (var button in buttons)
        {
            Assert.Equal(HandledCommands.Handles(button.Command), button.IsEnabled);
        }
    }

    [AvaloniaFact]
    public void FrenchLabelsFollowTheLanguage()
    {
        using var host = ShellHost.Open(Language.Fr);
        var buttons = host.Window.FunctionKeys;
        Assert.Equal("F3 Voir", buttons[0].Content);
        Assert.Equal("Alt+F4 Quitter", buttons[7].Content);
        Assert.Equal("Prêt", host.Window.StatusText);
    }

    [AvaloniaFact]
    public void AClickDispatchesTheCommandOfAnEnabledKey()
    {
        var copies = 0;
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IShellModule>(new CopyProbe(() => copies++)));
        var buttons = host.Window.FunctionKeys;
        var copy = Assert.Single(buttons, button => button.Command == CommandId.Copy);
        var view = Assert.Single(buttons, button => button.Command == CommandId.Search);
        Assert.True(copy.IsEnabled);
        Assert.False(view.IsEnabled);
        copy.Click();
        view.Click();
        Assert.Equal(1, copies);
    }

    private sealed class CopyProbe(Action copy) : IShellModule
    {
        public void Contribute(IShellContributions shell) => shell.Handle(CommandId.Copy, copy);
    }
}
