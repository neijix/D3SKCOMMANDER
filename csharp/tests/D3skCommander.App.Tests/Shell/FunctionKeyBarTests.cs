using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class FunctionKeyBarTests
{
    [AvaloniaFact]
    public void ShowsTheEightFunctionKeys()
    {
        using var host = ShellHost.Open(Language.En);
        var buttons = Buttons(host.Window);
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
            buttons.Select(button => (CommandId)button.Tag!).ToArray());
        foreach (var button in buttons)
        {
            Assert.Equal((CommandId)button.Tag! == CommandId.Quit, button.IsEnabled);
        }
    }

    [AvaloniaFact]
    public void FrenchLabelsFollowTheLanguage()
    {
        using var host = ShellHost.Open(Language.Fr);
        var buttons = Buttons(host.Window);
        Assert.Equal("F3 Voir", buttons[0].Content);
        Assert.Equal("Alt+F4 Quitter", buttons[7].Content);
        Assert.Equal("Prêt", host.Window.FindControl<TextBlock>("StatusLine")!.Text);
    }

    private static List<Button> Buttons(D3skCommander.App.Shell.MainWindow window)
    {
        var bar = window.FindControl<Border>("FunctionKeyBar");
        Assert.NotNull(bar);
        var grid = Assert.IsType<Grid>(bar.Child);
        return grid.Children.OfType<Button>().ToList();
    }
}
