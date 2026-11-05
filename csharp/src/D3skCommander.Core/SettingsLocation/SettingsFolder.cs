namespace D3skCommander.Core.SettingsLocation;

/// <summary>The folder settings resolve to, and why.</summary>
public sealed record SettingsFolder(string Dir, SettingsSource Source)
{
    /// <summary>True on a first launch: nobody chose a folder and no preferences exist there yet.</summary>
    public bool NeedsChoice() =>
        Source == SettingsSource.Default
        && !File.Exists(Path.Combine(Dir, SettingsFileNames.Preferences));
}
