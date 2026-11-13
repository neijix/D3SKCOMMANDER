using D3skCommander.Core.Commands;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.Core.Tests.Settings;

public class ThemeModesTests
{
    [Fact]
    public void EachModeHasItsOwnCommandAndTheCommandGivesTheModeBack()
    {
        Assert.Equal([ThemeMode.System, ThemeMode.Light, ThemeMode.Dark], ThemeModes.All);
        Assert.Equal(3, ThemeModes.All.Select(ThemeModes.Command).Distinct().Count());
        foreach (var mode in ThemeModes.All)
        {
            Assert.Equal(mode, ThemeModes.FromCommand(ThemeModes.Command(mode)));
        }

        Assert.Null(ThemeModes.FromCommand(CommandId.Copy));
    }

    [Fact]
    public void TheThemeEntriesAreInTheShowMenu()
    {
        var show = CommandCatalog.Menus.Single(m => m.Items.Contains(CommandId.ShowToolbar));
        Assert.All(ThemeModes.All, mode => Assert.Contains(ThemeModes.Command(mode), show.Items));
    }
}
