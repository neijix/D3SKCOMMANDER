using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.Core.Tests.Settings;

/// <summary>AC1 and AC3: the settings categories, their names and the deep-link commands.</summary>
public class SettingsCategoriesTests
{
    [Fact]
    public void ElevenCategoriesInTheDesignOrder()
    {
        Assert.Equal(
            ["general", "panels", "file-operations", "programs", "columns", "favorite-tabs",
             "file-associations", "toolbar", "user-commands", "quick-find-index", "settings-folder"],
            SettingsCategories.All.Select(c => c.Id));
    }

    [Fact]
    public void IdsAndNameKeysAreUnique()
    {
        Assert.Equal(11, SettingsCategories.All.Select(c => c.Id).Distinct().Count());
        Assert.Equal(11, SettingsCategories.All.Select(c => c.NameKey).Distinct().Count());
    }

    [Theory]
    [InlineData(Language.En)]
    [InlineData(Language.Fr)]
    public void EveryNameKeyHasText(Language language)
    {
        var localizer = Localizer.Load(settingsLanguage: language, systemLanguage: language);
        foreach (var category in SettingsCategories.All)
        {
            var text = localizer.Get(category.NameKey, language);
            Assert.NotEqual(category.NameKey, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void NamesMatchTheExpectedTexts()
    {
        var en = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        Assert.Equal(
            ["General", "Panels", "File operations", "Programs", "Columns", "Favorite tabs",
             "File associations", "Toolbar", "User commands", "Quick find index", "Settings folder"],
            SettingsCategories.All.Select(c => en.Get(c.NameKey, Language.En)));
        Assert.Equal("Onglets favoris", en.Get("favorites.title", Language.Fr));
        Assert.Equal("Barre d'outils", en.Get(SettingsCategories.All[7].NameKey, Language.Fr));
        Assert.Equal("Associations de fichiers", en.Get(SettingsCategories.All[6].NameKey, Language.Fr));
    }

    [Fact]
    public void DeepLinkCommandsMapToTheirCategory()
    {
        Assert.Equal("columns", SettingsCategories.ForCommand(CommandId.EditColumnSets)?.Id);
        Assert.Equal("toolbar", SettingsCategories.ForCommand(CommandId.CustomizeToolbar)?.Id);
        Assert.Equal("favorite-tabs", SettingsCategories.ForCommand(CommandId.ManageFavoriteTabs)?.Id);
        Assert.Equal("file-associations", SettingsCategories.ForCommand(CommandId.EditFileAssociations)?.Id);
    }

    [Fact]
    public void EveryOtherCommandHasNoCategory()
    {
        CommandId[] linked = [CommandId.EditColumnSets, CommandId.CustomizeToolbar, CommandId.ManageFavoriteTabs, CommandId.EditFileAssociations];
        foreach (var command in Enum.GetValues<CommandId>().Except(linked))
            Assert.Null(SettingsCategories.ForCommand(command));
    }
}
