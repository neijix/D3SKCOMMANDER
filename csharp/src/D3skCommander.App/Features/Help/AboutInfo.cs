using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Help;

/// <summary>The About text: name, version and repository from the assembly metadata, license, runtime versions.</summary>
public static class AboutInfo
{
    private const string RepositoryKey = "RepositoryUrl";

    public static string Version()
    {
        var assembly = typeof(AboutInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return TrimBuild(informational);
        }

        return assembly.GetName().Version?.ToString(3) ?? "0";
    }

    public static string Repository()
    {
        foreach (var data in typeof(AboutInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (data.Key == RepositoryKey && !string.IsNullOrWhiteSpace(data.Value))
            {
                return data.Value;
            }
        }

        return "";
    }

    public static string AvaloniaVersion()
    {
        var assembly = typeof(Avalonia.Application).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return TrimBuild(informational);
        }

        return assembly.GetName().Version?.ToString(3) ?? "?";
    }

    public static IReadOnlyList<string> Lines(ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return
        [
            text.Get("help.about.name"),
            text.Get("help.about.tagline"),
            Format(text, "help.about.version", Version()),
            Format(text, "help.about.repository", Repository()),
            text.Get("help.about.license"),
            Format(text, "help.about.runtime", RuntimeInformation.FrameworkDescription),
            Format(text, "help.about.avalonia", AvaloniaVersion()),
        ];
    }

    public static string Body(ILocalizer text) => string.Join("\n", Lines(text));

    /// <summary>"0.2.1+abc123" becomes "0.2.1": the build metadata after '+' is not part of the version.</summary>
    private static string TrimBuild(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    private static string Format(ILocalizer text, string key, string value) =>
        string.Format(CultureInfo.InvariantCulture, text.Get(key), value);
}
