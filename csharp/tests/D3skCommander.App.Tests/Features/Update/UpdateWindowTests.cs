using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3sk.Update.Keychain;
using D3sk.Update.SignIn;
using D3skCommander.App;
using D3skCommander.App.Features.Update;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Archives;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Install;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Update;

public class UpdateWindowTests
{
    private static readonly Localizer English = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
    private static readonly Localizer French = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);

    [AvaloniaFact]
    public async Task ANewerSignedReleaseIsShownNextToTheInstalledVersion()
    {
        var model = Model(new FakeReleases("0.3.0", Releases.Environment()), Releases.Environment(), out _);

        await model.CheckAsync();

        Assert.Equal("Installed version: 0.2.1", model.CurrentText);
        Assert.Equal("Latest version: 0.3.0", model.Latest);
        Assert.Equal("Version 0.3.0 is available.", model.Status);
        Assert.True(model.CanInstall);
    }

    [AvaloniaFact]
    public async Task TheSameVersionIsUpToDate()
    {
        var model = Model(new FakeReleases("0.2.1", Releases.Environment()), Releases.Environment(), out _);

        await model.CheckAsync();

        Assert.Equal(English.Get("update.up_to_date"), model.Status);
        Assert.False(model.CanInstall);
    }

    [AvaloniaFact]
    public async Task APrivateRepositoryAsksToSignInAndTheNetworkFailureSaysNoNetwork()
    {
        var environment = Releases.Environment();
        var needsSignIn = Model(new FakeReleases("0.3.0", environment) { PrivateRepository = true }, environment, out _);
        await needsSignIn.CheckAsync();
        Assert.Equal(English.Get("update.sign_in_needed"), needsSignIn.Status);
        Assert.False(needsSignIn.CanInstall);

        var offline = Model(new FakeReleases("0.3.0", environment) { Offline = true }, environment, out _, french: true);
        await offline.CheckAsync();
        Assert.Equal(French.Get("update.no_network"), offline.Status);
        Assert.NotEqual(English.Get("update.no_network"), offline.Status);
        Assert.False(offline.CanInstall);
    }

    [AvaloniaFact]
    public async Task AnUnavailableKeychainIsNamedWithItsReason()
    {
        var environment = Releases.Environment();
        var model = Model(new FakeReleases("0.3.0", environment) { PrivateRepository = true }, environment, out _, keychain: new BrokenKeychain());

        await model.CheckAsync();

        Assert.Equal(English.Get("update.keychain_unavailable").Replace("{reason}", "no secret service", StringComparison.Ordinal), model.Status);
        Assert.False(model.CanInstall);
    }

    [AvaloniaFact]
    public void ASignInFailureOfTheKeychainUsesTheKeychainText()
    {
        var environment = Releases.Environment();
        var releases = new FakeReleases("0.3.0", environment);
        using var service = releases.Service(new BrokenKeychain());
        var model = new UpdateViewModel(
            new UpdateChecker(service, environment), service, new RecordingRestarter(), English, Path.GetTempPath(), _ => { });

        var state = service.GetSignInState() with { Phase = SignInPhase.Failed, Reason = "keychain unavailable: no secret service" };

        Assert.Equal(
            English.Get("update.keychain_unavailable").Replace("{reason}", "no secret service", StringComparison.Ordinal),
            model.SignInLine(state));
    }

    [AvaloniaFact]
    public async Task ASourceBuildIsNeverOfferedAnInstall()
    {
        var environment = Releases.Environment(kind: new InstallKind.Source());
        var model = Model(new FakeReleases("0.3.0", environment), environment, out _);

        await model.CheckAsync();

        Assert.False(model.CanInstall);
        Assert.Contains(English.Get("update.source_build"), model.Status, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task InstallDownloadsVerifiesAndRestartsTheNewProgram()
    {
        using var temp = new TempFolder();
        var target = Path.Combine(temp.Path, "d3skcommander.AppImage");
        File.WriteAllText(target, "old");
        var environment = Releases.Environment(kind: new InstallKind.AppImage(target));
        var model = Model(new FakeReleases("0.3.0", environment, "new program"u8.ToArray()), environment, out var restarter, folder: temp.Path);

        await model.CheckAsync();
        Assert.True(model.CanInstall);
        await model.InstallAsync();

        Assert.Equal("new program", File.ReadAllText(target));
        Assert.Equal(Relaunch.Of(target), restarter.Started);
        Assert.Equal(English.Get("update.restarting"), model.Status);
    }

    [AvaloniaFact]
    public async Task AReleaseWithoutASignatureIsRefusedAndInstallsNothing()
    {
        using var temp = new TempFolder();
        var target = Path.Combine(temp.Path, "d3skcommander.AppImage");
        File.WriteAllText(target, "old");
        var environment = Releases.Environment(kind: new InstallKind.AppImage(target));
        var model = Model(new FakeReleases("0.3.0", environment) { Sign = false }, environment, out var restarter, folder: temp.Path);

        await model.CheckAsync();
        await model.InstallAsync();

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Null(restarter.Started);
        Assert.Equal(English.Get("update.release_not_signed"), model.Status);
    }

    [AvaloniaFact]
    public async Task ATamperedPackageIsRefusedAndInstallsNothing()
    {
        using var temp = new TempFolder();
        var target = Path.Combine(temp.Path, "d3skcommander.AppImage");
        File.WriteAllText(target, "old");
        var environment = Releases.Environment(kind: new InstallKind.AppImage(target));
        var model = Model(new FakeReleases("0.3.0", environment) { TamperPackage = true }, environment, out var restarter, french: true, folder: temp.Path);

        await model.CheckAsync();
        await model.InstallAsync();

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Null(restarter.Started);
        Assert.Equal(French.Get("update.checksum_mismatch"), model.Status);
    }

    [AvaloniaFact]
    public async Task ASetupInstallInAFolderNeedingAdministratorRightsSaysWindowsWillAsk()
    {
        var environment = Releases.Environment(kind: new InstallKind.WindowsSetup(@"C:\Program Files\D3SK\d3skcommander.exe"));
        var releases = new FakeReleases("0.3.0", environment);
        var asks = Model(releases, environment, out _, needsAdministrator: _ => true);
        await asks.CheckAsync();
        Assert.Contains("Windows will ask for administrator rights", asks.Status, StringComparison.Ordinal);
        Assert.Contains(English.Get("update.needs_admin"), asks.Status, StringComparison.Ordinal);
        Assert.True(asks.CanInstall);

        var french = Model(releases, environment, out _, french: true, needsAdministrator: _ => true);
        await french.CheckAsync();
        Assert.Contains(French.Get("update.needs_admin"), french.Status, StringComparison.Ordinal);
        Assert.NotEqual(English.Get("update.needs_admin"), French.Get("update.needs_admin"));

        var quiet = Model(releases, environment, out _, needsAdministrator: _ => false);
        await quiet.CheckAsync();
        Assert.DoesNotContain(English.Get("update.needs_admin"), quiet.Status, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ALinuxScriptUpdateRewritesTheVersionOfTheInstallRecord()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Path, "apps", "d3skcommander");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "D3skCommander"), "v1");
        File.WriteAllText(Path.Combine(folder, InstallRecord.FileName), "kind=linux-script\nversion=0.2.1\n");
        var environment = Releases.Environment(kind: new InstallKind.LinuxScript(folder));
        var model = Model(new FakeReleases("0.3.0", environment, LinuxArchive(temp.Path)), environment, out var restarter, folder: temp.Path);

        await model.CheckAsync();
        await model.InstallAsync();

        Assert.Equal(["kind=linux-script", "version=0.3.0"], File.ReadAllLines(Path.Combine(folder, InstallRecord.FileName)));
        Assert.Equal(Relaunch.Of(Path.Combine(folder, "D3skCommander")), restarter.Started);
    }

    [AvaloniaFact]
    public async Task ANotWritableFolderShowsTheLocalizedTextWithTheFolder()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Path, "d3skcommander");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "D3skCommander"), "v1");
        Directory.CreateDirectory(Path.Combine(folder, ".d3sk-write-test"));
        var environment = Releases.Environment(kind: new InstallKind.LinuxScript(folder));
        var model = Model(new FakeReleases("0.3.0", environment, LinuxArchive(temp.Path)), environment, out var restarter, french: true, folder: temp.Path);

        await model.CheckAsync();
        await model.InstallAsync();

        Assert.Equal(French.Get("update.not_writable").Replace("{folder}", folder, StringComparison.Ordinal), model.Status);
        Assert.Null(restarter.Started);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(folder, "D3skCommander")));
    }

    [AvaloniaFact]
    public async Task CheckUpdateOpensTheUpdateToolWindowWithTheVersions()
    {
        using var provider = TestComposition.Build(English);
        var main = provider.GetRequiredService<MainWindow>();
        main.Show();
        var tools = provider.GetRequiredService<IToolWindowHost>();
        var shell = provider.GetRequiredService<IShellContributions>();
        var commands = Commands(new FakeReleases("0.3.0", Releases.Environment()), Releases.Environment());
        tools.Register(ToolKind.Update, commands.CreateView);
        Assert.True(shell.CanExecute(CommandId.CheckUpdate));

        Assert.True(shell.Dispatch(CommandId.CheckUpdate));

        Assert.True(tools.IsOpen(ToolKind.Update));
        var window = tools.Open(ToolKind.Update);
        var model = Assert.IsType<UpdateViewModel>(window.View);
        await WaitFor(() => model.Latest.Length > 0);
        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Installed version: 0.2.1", labels);
        Assert.Contains("Latest version: 0.3.0", labels);
        window.Close();
        main.Close();
    }

    [Fact]
    public async Task StartupCheckIsSkippedUnderTheScreenshotHook()
    {
        Assert.False(UpdateChecker.ShouldCheckAtStart(checkUpdates: true, screenshotHookActive: true, disabledByEnvironment: false));
        Assert.False(UpdateChecker.ShouldCheckAtStart(checkUpdates: false, screenshotHookActive: false, disabledByEnvironment: false));
        Assert.False(UpdateChecker.ShouldCheckAtStart(checkUpdates: true, screenshotHookActive: false, disabledByEnvironment: true));
        Assert.True(UpdateChecker.ShouldCheckAtStart(checkUpdates: true, screenshotHookActive: false, disabledByEnvironment: false));

        var releases = new FakeReleases("0.3.0", Releases.Environment());
        var hooked = Commands(releases, Releases.Environment(), screenshotActive: true);
        Assert.False(await hooked.CheckAtStartAsync());
        Assert.Empty(releases.Seen);
        Assert.False(hooked.MenuItem.IsShown);
    }

    [Fact]
    public async Task TheQuietStartupCheckShowsTheMenuBarItemForANewerRelease()
    {
        var releases = new FakeReleases("0.3.0", Releases.Environment());
        var commands = Commands(releases, Releases.Environment());

        Assert.True(await commands.CheckAtStartAsync());

        Assert.NotEmpty(releases.Seen);
        Assert.Equal("Update to 0.3.0", commands.MenuItem.Header);

        var quiet = Commands(new FakeReleases("0.3.0", Releases.Environment()) { Offline = true }, Releases.Environment());
        Assert.True(await quiet.CheckAtStartAsync());
        Assert.False(quiet.MenuItem.IsShown);
    }

    [Fact]
    public async Task TheQuietStartupCheckWithoutATokenOnlyShowsSignInNeededAndNeverStartsTheDeviceFlow()
    {
        var releases = new FakeReleases("0.3.0", Releases.Environment()) { PrivateRepository = true };
        var commands = Commands(releases, Releases.Environment());

        Assert.True(await commands.CheckAtStartAsync());

        Assert.Equal(English.Get("update.sign_in_needed"), commands.MenuItem.Header);
        Assert.DoesNotContain(releases.Seen, s => s.StartsWith("POST", StringComparison.Ordinal));
        Assert.DoesNotContain(releases.Seen, s => s.Contains("github.com/login", StringComparison.Ordinal));
    }

    private static byte[] LinuxArchive(string root)
    {
        var stage = Path.Combine(root, "stage", "d3skcommander");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "D3skCommander"), "v2");
        var archive = Path.Combine(root, "linux.tar.gz");
        var created = ArchiveIo.Create(archive, ArchiveKind.TarGz, [stage], 6, null, TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return File.ReadAllBytes(archive);
    }

    private static UpdateViewModel Model(
        FakeReleases releases,
        UpdateEnvironment environment,
        out RecordingRestarter restarter,
        bool french = false,
        IKeychain? keychain = null,
        string? folder = null,
        Func<InstallKind, bool>? needsAdministrator = null)
    {
        restarter = new RecordingRestarter();
        var service = releases.Service(keychain);
        return new UpdateViewModel(
            new UpdateChecker(service, environment),
            service,
            restarter,
            french ? French : English,
            Path.Combine(folder ?? Path.GetTempPath(), "d3sk-update-test"),
            _ => { },
            needsAdministrator);
    }

    private static UpdateCommands Commands(FakeReleases releases, UpdateEnvironment environment, bool screenshotActive = false)
    {
        using var folder = new TempFolder();
        var service = releases.Service();
        return new UpdateCommands(
            new UpdateChecker(service, environment),
            service,
            new RecordingRestarter(),
            new NoTools(),
            English,
            new PreferencesStore(folder.Path),
            () => screenshotActive,
            () => false);
    }

    private static async Task WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(done());
    }

    private sealed class NoTools : IToolWindowHost
    {
        public void Register(ToolKind kind, Func<IShellWidget> content)
        {
        }

        public ToolWindow Open(ToolKind kind) => throw new NotSupportedException();

        public bool IsOpen(ToolKind kind) => false;
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder() => Path = Directory.CreateTempSubdirectory("d3sk-update-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
