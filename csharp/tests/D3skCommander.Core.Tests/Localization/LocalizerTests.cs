using System.Globalization;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.Core.Tests.Localization;

public class LocalizerTests
{
    [Fact]
    public void KeysAreUniqueAndTranslated()
    {
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var keys = localizer.Keys.ToList();
        var len = keys.Count;
        keys.Sort(StringComparer.Ordinal);
        var unique = keys.Distinct(StringComparer.Ordinal).Count();
        Assert.Equal(len, unique);
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.False(string.IsNullOrEmpty(localizer.Get(key, Language.En)));
            Assert.False(string.IsNullOrEmpty(localizer.Get(key, Language.Fr)));
        }
    }

    [Fact]
    public void CommandLabelsReadDifferentlyInEachLanguage()
    {
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var cmds = localizer.Keys.Where(key => key.StartsWith("cmd.", StringComparison.Ordinal)).ToList();
        // The 79 v0.1 commands, plus cmd.open, which the old table also carried.
        Assert.True(cmds.Count >= 79, $"expected the v0.1 command labels, found {cmds.Count}");
        for (var i = 0; i < cmds.Count; i++)
        {
            var english = localizer.Get(cmds[i], Language.En);
            var french = localizer.Get(cmds[i], Language.Fr);
            for (var j = i + 1; j < cmds.Count; j++)
            {
                var otherEnglish = localizer.Get(cmds[j], Language.En);
                var otherFrench = localizer.Get(cmds[j], Language.Fr);
                Assert.True(
                    english != otherEnglish && french != otherFrench,
                    $"{cmds[i]} and {cmds[j]} share a label");
            }
        }
    }

    [Fact]
    public void SwitchesLanguage()
    {
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        localizer.Use(Language.Fr);
        Assert.Equal("Annuler", localizer.Get("cancel"));
        Assert.Equal("Annuler", localizer.Text("Cancel", "Annuler"));
        localizer.Use(Language.En);
        Assert.Equal("Cancel", localizer.Get("cancel"));
        Assert.Equal("Cancel", localizer.Text("Cancel", "Annuler"));
        Assert.Equal("no.such.key", localizer.Get("no.such.key"));
    }

    [Fact]
    public void UsesTheSettingsLanguageOrElseTheSystemLanguage()
    {
        ILocalizer fromSettings = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.En);
        Assert.Equal(Language.Fr, fromSettings.Language);
        Assert.Equal("Annuler", fromSettings.Get("cancel"));

        ILocalizer fromSystem = Localizer.Load(settingsLanguage: null, systemLanguage: Language.Fr);
        Assert.Equal(Language.Fr, fromSystem.Language);
        Assert.Equal("Annuler", fromSystem.Get("cancel"));

        ILocalizer english = Localizer.Load(settingsLanguage: null, systemLanguage: Language.En);
        Assert.Equal(Language.En, english.Language);
        Assert.Equal("Cancel", english.Get("cancel"));
    }

    [Fact]
    public void SystemLanguageFollowsTheLocaleOrElseTheUiCulture()
    {
        Assert.Equal(Language.Fr, Languages.FromLocale(Env(("LANG", "fr_FR.UTF-8")), new CultureInfo("en-US")));
        Assert.Equal(Language.Fr, Languages.FromLocale(Env(("LC_ALL", "fr_CA")), new CultureInfo("en-US")));
        Assert.Equal(Language.Fr, Languages.FromLocale(Env(("LC_MESSAGES", "FR")), new CultureInfo("en-US")));
        Assert.Equal(
            Language.Fr,
            Languages.FromLocale(Env(("LC_ALL", "en_US"), ("LANG", "fr_FR")), new CultureInfo("en-US")));
        Assert.Equal(Language.En, Languages.FromLocale(Env(("LANG", "en_US.UTF-8")), new CultureInfo("fr-FR")));
        Assert.Equal(Language.En, Languages.FromLocale(Env(("LC_ALL", "C")), new CultureInfo("fr-FR")));
        Assert.Equal(Language.Fr, Languages.FromLocale(new Dictionary<string, string?>(), new CultureInfo("fr-FR")));
        Assert.Equal(Language.En, Languages.FromLocale(new Dictionary<string, string?>(), new CultureInfo("en-US")));
    }

    [Fact]
    public void AKeyWithoutBothLanguagesIsRejected()
    {
        var missing = Assert.Throws<FormatException>(() => StringCatalog.Parse("""{ "only.en": { "en": "Only" } }"""));
        Assert.Contains("only.en", missing.Message);
        var empty = Assert.Throws<FormatException>(() =>
            StringCatalog.Parse("""{ "empty.fr": { "en": "X", "fr": "" } }"""));
        Assert.Contains("empty.fr", empty.Message);
    }

    [Fact]
    public void DuplicateKeysAreRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StringCatalog.Parse(
            """
            { "cancel": { "en": "Cancel", "fr": "Annuler" }, "cancel": { "en": "Cancel", "fr": "Annuler" } }
            """));
        Assert.Contains("duplicate i18n key", ex.Message);

        var left = StringCatalog.Parse("""{ "cancel": { "en": "Cancel", "fr": "Annuler" } }""");
        var right = StringCatalog.Parse("""{ "cancel": { "en": "Cancel", "fr": "Annuler" } }""");
        var merged = Assert.Throws<InvalidOperationException>(() => StringCatalog.Merge([left, right]));
        Assert.Contains("duplicate i18n key", merged.Message);
    }

    private static Dictionary<string, string?> Env(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => (string?)pair.Value);
}
