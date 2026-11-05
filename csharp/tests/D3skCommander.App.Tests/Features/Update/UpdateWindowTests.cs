using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App;
using D3skCommander.App.Features.Update;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Install;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Update;

public class UpdateWindowTests
{
    private static readonly Localizer English = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    [AvaloniaFact]
    public async Task ANewerReleaseIsShownNextToTheInstalledVersion()
    {
        var feed = new FakeFeed { Answer = Releases.Ok("0.3.0", Asset("d3skcommander-0.3.0-linux-x86_64.tar.gz", [1])) };
        var model = Model(feed, Releases.Environment(), out _);

        await model.CheckAsync();

        Assert.Equal("Installed version: 0.2.1", model.CurrentText);
        Assert.Equal("Latest version: 0.3.0", model.Latest);
        Assert.Equal("Version 0.3.0 is available.", model.Status);
        Assert.True(model.CanInstall);
        Assert.Equal("Fixes things.", model.Notes);
    }

    [AvaloniaFact]
    public async Task TheSameVersionIsUpToDate()
    {
        var feed = new FakeFeed { Answer = Releases.Ok("0.2.1") };
        var model = Model(feed, Releases.Environment(), out _);

        await model.CheckAsync();

        Assert.Equal(English.Get("update.up_to_date"), model.Status);
        Assert.False(model.CanInstall);
    }

    [AvaloniaFact]
    public async Task APrivateRepositoryIsAClearMessageNotACrash()
    {
        var notFound = new FakeFeed
        {
            Answer = UpdateStep<GitHubRelease>.Fail("no published release at x (none yet, or the repository is private)"),
        };
        var model = Model(notFound, Releases.Environment(), out _);
        await model.CheckAsync();
        Assert.Equal(English.Get("update.check_failed"), model.Status);
        Assert.DoesNotContain("x (none", model.Status, StringComparison.Ordinal);

        var offline = new FakeFeed { Throw = new HttpRequestException("name resolution") };
        var other = Model(offline, Releases.Environment(), out _);
        await other.CheckAsync();
        Assert.Equal(English.Get("update.check_failed"), other.Status);
        Assert.False(other.CanInstall);
    }

    [AvaloniaFact]
    public async Task ASourceBuildIsNeverOfferedAnInstall()
    {
        var feed = new FakeFeed { Answer = Releases.Ok("0.3.0", Asset("d3skcommander-0.3.0-linux-x86_64.tar.gz", [1])) };
        var model = Model(feed, Releases.Environment(kind: new InstallKind.Source()), out _);

        await model.CheckAsync();

        Assert.False(model.CanInstall);
        Assert.Contains(English.Get("update.source_build"), model.Status, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task InstallDownloadsChecksAndRestartsTheNewProgram()
    {
        using var temp = new TempFolder();
        var target = Path.Combine(temp.Path, "d3skcommander.AppImage");
        File.WriteAllText(target, "old");
        var bytes = "new program"u8.ToArray();
        var name = "d3skcommander-0.3.0-linux-x86_64.AppImage";
        var asset = new ReleaseAsset(name, "https://example.invalid/" + name, bytes.Length, ReleaseChecksum.Of(bytes));
        var feed = new FakeFeed { Answer = Releases.Ok("0.3.0", asset) };
        var model = Model(feed, Releases.Environment(kind: new InstallKind.AppImage(target)), out var restarter, bytes, temp.Path);

        await model.CheckAsync();
        Assert.True(model.CanInstall);
        await model.InstallAsync();

        Assert.Equal("new program", File.ReadAllText(target));
        Assert.Equal(Relaunch.Of(target), restarter.Started);
        Assert.Equal(English.Get("update.restarting"), model.Status);
    }

    [AvaloniaFact]
    public async Task AWrongChecksumInstallsNothing()
    {
        using var temp = new TempFolder();
        var target = Path.Combine(temp.Path, "d3skcommander.AppImage");
        File.WriteAllText(target, "old");
        var name = "d3skcommander-0.3.0-linux-x86_64.AppImage";
        var asset = new ReleaseAsset(name, "https://example.invalid/" + name, 3, new string('0', 64));
        var feed = new FakeFeed { Answer = Releases.Ok("0.3.0", asset) };
        var model = Model(feed, Releases.Environment(kind: new InstallKind.AppImage(target)), out var restarter, "abc"u8.ToArray(), temp.Path);

        await model.CheckAsync();
        await model.InstallAsync();

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Null(restarter.Started);
        Assert.StartsWith(English.Get("update.failed"), model.Status, StringComparison.Ordinal);
        Assert.Contains(English.Get("error.invalid_data"), model.Status, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task CheckUpdateOpensTheUpdateToolWindowWithTheVersions()
    {
        var feed = new FakeFeed { Answer = Releases.Ok("0.3.0", Asset("d3skcommander-0.3.0-linux-x86_64.tar.gz", [1])) };
        using var provider = TestComposition.Build(English);
        var main = provider.GetRequiredService<MainWindow>();
        main.Show();
        var tools = provider.GetRequiredService<IToolWindowHost>();
        var shell = provider.GetRequiredService<IShellContributions>();
        var commands = Commands(feed, Releases.Environment(), out _);
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

        var feed = new FakeFeed { Answer = Releases.Ok("0.3.0") };
        var hooked = Commands(feed, Releases.Environment(), out _, screenshotActive: true);
        Assert.False(await hooked.CheckAtStartAsync());
        Assert.Equal(0, feed.Calls);
        Assert.False(hooked.MenuItem.IsShown);
    }

    [Fact]
    public async Task TheQuietStartupCheckShowsTheMenuBarItemForANewerRelease()
    {
        var feed = new FakeFeed { Answer = Releases.Ok("0.3.0") };
        var commands = Commands(feed, Releases.Environment(), out _);

        Assert.True(await commands.CheckAtStartAsync());

        Assert.Equal(1, feed.Calls);
        Assert.Equal("Update to 0.3.0", commands.MenuItem.Header);

        var failing = new FakeFeed { Throw = new HttpRequestException("offline") };
        var quiet = Commands(failing, Releases.Environment(), out _);
        Assert.True(await quiet.CheckAtStartAsync());
        Assert.False(quiet.MenuItem.IsShown);
    }

    private static ReleaseAsset Asset(string name, byte[] bytes) =>
        new(name, "https://example.invalid/" + name, bytes.Length, ReleaseChecksum.Of(bytes));

    private static UpdateViewModel Model(
        FakeFeed feed,
        UpdateEnvironment environment,
        out RecordingRestarter restarter,
        byte[]? bytes = null,
        string? folder = null)
    {
        restarter = new RecordingRestarter();
        var service = new UpdateService(new JobService(), new BytesSource(bytes ?? []));
        var checker = new UpdateChecker(feed, environment);
        return new UpdateViewModel(
            checker,
            feed,
            service,
            restarter,
            English,
            Path.Combine(folder ?? Path.GetTempPath(), "d3sk-update-test"),
            _ => { });
    }

    private static UpdateCommands Commands(
        FakeFeed feed,
        UpdateEnvironment environment,
        out RecordingRestarter restarter,
        bool screenshotActive = false)
    {
        restarter = new RecordingRestarter();
        using var folder = new TempFolder();
        var prefs = new PreferencesStore(folder.Path);
        return new UpdateCommands(
            new UpdateChecker(feed, environment),
            feed,
            new UpdateService(new JobService(), new BytesSource([])),
            restarter,
            new NoTools(),
            English,
            prefs,
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
