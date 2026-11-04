using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class FunctionKeyBarTests
{
    [Fact]
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

    [Fact]
    public void FrenchLabelsFollowTheLanguage()
    {
        using var host = ShellHost.Open(Language.Fr);
        var buttons = host.Window.FunctionKeys;
        Assert.Equal("F3 Voir", buttons[0].Content);
        Assert.Equal("Alt+F4 Quitter", buttons[7].Content);
        Assert.Equal("Prêt", host.Window.StatusText);
    }
}
