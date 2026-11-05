using D3skCommander.App.Features.Settings;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.App.Tests.Features.Settings;

/// <summary>A throw-away settings folder with its stores, the dialogs and a log, for the settings tests.</summary>
internal sealed class SettingsHarness : IDisposable
{
    public SettingsHarness(SettingsSource source = SettingsSource.Chosen, Language language = Language.En)
    {
        Root = Directory.CreateTempSubdirectory("d3sk-settings-").FullName;
        Folder = Path.Combine(Root, "current");
        Home = Path.Combine(Root, "home");
        Directory.CreateDirectory(Home);
        Settings = new AppSettings(new SettingsFolder(Folder, source));
        Text = Localizer.Load(settingsLanguage: language, systemLanguage: language);
        Dialogs = new FakeDialogService();
        Log = new OperationLog();
    }

    public string Root { get; }

    public string Folder { get; }

    public string Home { get; }

    public string Install => Path.Combine(Root, "install");

    public AppSettings Settings { get; }

    public Localizer Text { get; }

    public FakeDialogService Dialogs { get; }

    public OperationLog Log { get; }

    public SettingsPlaces Places => new()
    {
        PlatformDir = Home,
        InstallDir = Install,
        InstallWritable = true,
    };

    public SettingsViewModel NewView() => new(Settings, Log, Text, Dialogs, () => { });

    public SettingsFolderCommands NewFolderCommands(bool screenshotActive = false) =>
        new(Settings, Dialogs, Text, () => screenshotActive, () => Places);

    public void Dispose()
    {
        Settings.Dispose();
        Directory.Delete(Root, true);
    }
}
