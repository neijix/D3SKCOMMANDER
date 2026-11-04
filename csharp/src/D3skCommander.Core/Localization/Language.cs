using System.Globalization;

namespace D3skCommander.Core.Localization;

public enum Language
{
    En,
    Fr,
}

/// <summary>Picks English or French from the settings, otherwise from the system language.</summary>
public static class Languages
{
    // v0.1 Lang::from_env reads these and selects French when any value starts with "fr".
    private static readonly string[] LocaleVariables = ["LC_ALL", "LC_MESSAGES", "LANG"];

    public static Language Choose(Language? settingsLanguage, Language? systemLanguage = null) =>
        settingsLanguage ?? systemLanguage ?? Detect();

    public static Language Detect()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in LocaleVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                environment[name] = value;
            }
        }

        return FromLocale(environment, CultureInfo.CurrentUICulture);
    }

    /// <summary>
    /// French when any set locale variable starts with "fr" (v0.1). When none of them is set,
    /// the UI culture decides; a variable that is set but not French stays English.
    /// </summary>
    public static Language FromLocale(IReadOnlyDictionary<string, string?> environment, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(culture);
        var sawLocale = false;
        foreach (var name in LocaleVariables)
        {
            if (!environment.TryGetValue(name, out var value) || value is null)
            {
                continue;
            }

            sawLocale = true;
            if (value.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
            {
                return Language.Fr;
            }
        }

        if (sawLocale)
        {
            return Language.En;
        }

        return culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase)
            ? Language.Fr
            : Language.En;
    }
}
