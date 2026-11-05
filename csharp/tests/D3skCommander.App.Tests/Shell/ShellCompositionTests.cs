using Avalonia.Headless.XUnit;
using D3skCommander.App;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Archives;
using D3skCommander.App.Features.Catalog;
using D3skCommander.App.Features.Clipboard;
using D3skCommander.App.Features.Compare;
using D3skCommander.App.Features.Crypt;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Ftp;
using D3skCommander.App.Features.Help;
using D3skCommander.App.Features.Jobs;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Log;
using D3skCommander.App.Features.MultiRename;
using D3skCommander.App.Features.NetDrives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickView;
using D3skCommander.App.Features.Search;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Features.Sync;
using D3skCommander.App.Features.Tools;
using D3skCommander.App.Features.Update;
using D3skCommander.App.Shell;
using D3skCommander.App.Smoke;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.ToolWindows;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class ShellCompositionTests
{
    [AvaloniaFact]
    public void CompositionRegistersEveryModule()
    {
        using var provider = Program.BuildProvider(Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En));
        Assert.IsType<ArchivesModule>(provider.GetRequiredService<ArchivesModule>());
        Assert.IsType<CatalogModule>(provider.GetRequiredService<CatalogModule>());
        Assert.IsType<ClipboardModule>(provider.GetRequiredService<ClipboardModule>());
        Assert.IsType<CompareModule>(provider.GetRequiredService<CompareModule>());
        Assert.IsType<CryptModule>(provider.GetRequiredService<CryptModule>());
        Assert.IsType<DrivesModule>(provider.GetRequiredService<DrivesModule>());
        Assert.IsType<EditorModule>(provider.GetRequiredService<EditorModule>());
        Assert.IsType<FileOpsModule>(provider.GetRequiredService<FileOpsModule>());
        Assert.IsType<FtpModule>(provider.GetRequiredService<FtpModule>());
        Assert.IsType<HelpModule>(provider.GetRequiredService<HelpModule>());
        Assert.IsType<JobsModule>(provider.GetRequiredService<JobsModule>());
        Assert.IsType<LaunchModule>(provider.GetRequiredService<LaunchModule>());
        Assert.IsType<ListerModule>(provider.GetRequiredService<ListerModule>());
        Assert.IsType<LogModule>(provider.GetRequiredService<LogModule>());
        Assert.IsType<MultiRenameModule>(provider.GetRequiredService<MultiRenameModule>());
        Assert.IsType<NetDrivesModule>(provider.GetRequiredService<NetDrivesModule>());
        Assert.IsType<PanelsModule>(provider.GetRequiredService<PanelsModule>());
        Assert.IsType<QuickViewModule>(provider.GetRequiredService<QuickViewModule>());
        Assert.IsType<SearchModule>(provider.GetRequiredService<SearchModule>());
        Assert.IsType<SettingsModule>(provider.GetRequiredService<SettingsModule>());
        Assert.IsType<SyncModule>(provider.GetRequiredService<SyncModule>());
        Assert.IsType<ToolsModule>(provider.GetRequiredService<ToolsModule>());
        Assert.IsType<UpdateModule>(provider.GetRequiredService<UpdateModule>());
        Assert.IsType<DialogsModule>(provider.GetRequiredService<DialogsModule>());
        Assert.IsType<ToolWindowsModule>(provider.GetRequiredService<ToolWindowsModule>());
        Assert.IsType<SmokeModule>(provider.GetRequiredService<SmokeModule>());
        Assert.IsType<StartupModule>(provider.GetRequiredService<StartupModule>());
    }

    [AvaloniaFact]
    public void ARegisteredFeaturesCommandDispatches()
    {
        var probe = new DispatchProbe();
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IShellModule>(probe));
        host.Window.Dispatch(CommandId.Copy);
        host.Window.KeyPress(GestureKey.F5, GestureModifiers.None);
        var button = Assert.Single(host.Window.FunctionKeys, item => item.Command == CommandId.Copy);
        Assert.True(button.IsEnabled);
        button.Click();
        Assert.Equal(3, probe.Copies);
        Assert.True(probe.Started);
        var under = Assert.Single(host.Window.UnderPanels, item => item.Name == "ProbeRegion");
        Assert.Equal("ProbeRegion", under.Name);
        Assert.IsType<LabelWidget>(under);
        var trailing = Assert.Single(host.Window.MenuBarTrailing);
        Assert.Equal("probe", Assert.IsType<MenuExtraWidget>(trailing).Header);
    }

    [AvaloniaFact]
    public void QuitClosesTheWindow()
    {
        using var host = ShellHost.Open(Language.En);
        host.Window.KeyPress(GestureKey.F4, GestureModifiers.Alt);
        Assert.False(host.Window.IsVisible);
    }

    [AvaloniaFact]
    public void StartupActionsRunOnceAfterTheWindowOpens()
    {
        var probe = new StartupProbe();
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IShellModule>(probe));
        Assert.Equal(1, probe.Runs);
        Assert.True(host.Window.IsVisible);
        host.Window.Close();
        Assert.Equal(1, probe.Runs);
    }

    [AvaloniaFact]
    public void QuitClosesTheOwnedWindowsBeforeTheMainWindow()
    {
        using var host = ShellHost.Open(Language.En);
        var tool = host.Provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.Log);
        var toolClosedWhenTheMainWindowCloses = false;
        host.Window.Closing += (_, _) => toolClosedWhenTheMainWindowCloses = !tool.IsVisible && host.Window.IsVisible;
        host.Window.Dispatch(CommandId.Quit);
        Assert.True(toolClosedWhenTheMainWindowCloses);
        Assert.False(host.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task ARefusingWindowStopsQuitAndTheMainWindowsCloseButton()
    {
        using var host = ShellHost.Open(Language.En);
        var tools = host.Provider.GetRequiredService<IToolWindowHost>();
        var kind = ToolKind.Define("refuser", "tool.log.title", 480, 320);
        var guard = new GuardWidget();
        tools.Register(kind, () => guard);
        var other = tools.Open(ToolKind.Log);
        var refusing = tools.Open(kind);

        host.Window.Dispatch(CommandId.Quit);
        guard.Answer(false);
        await Settle();
        Assert.True(host.Window.IsVisible);
        Assert.True(refusing.IsVisible);

        host.Window.Close();
        guard.Answer(false);
        await Settle();
        Assert.True(host.Window.IsVisible);
        Assert.True(refusing.IsVisible);
        Assert.True(other.IsVisible);
        Assert.Equal(2, guard.Asked);

        host.Window.Close();
        guard.Answer(true);
        for (var i = 0; i < 100 && host.Window.IsVisible; i++)
        {
            await Settle();
        }

        Assert.False(host.Window.IsVisible, "the main window closes once the refusing window agrees");
        Assert.False(refusing.IsVisible);
        Assert.False(other.IsVisible);
    }

    private static async Task Settle()
    {
        await Task.Delay(10, TestContext.Current.CancellationToken);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private sealed class StartupProbe : IShellModule
    {
        public int Runs { get; private set; }

        public void Contribute(IShellContributions shell) => shell.OnStartup(() => Runs++);
    }

    private sealed class DispatchProbe : IShellModule
    {
        public int Copies { get; private set; }

        public bool Started { get; private set; }

        public void Contribute(IShellContributions shell)
        {
            shell.Handle(CommandId.Copy, () => Copies++);
            shell.OnStartup(() => Started = true);
            shell.AddRegion(ShellRegion.UnderPanels, () => new LabelWidget("ProbeRegion", " "));
            shell.AppendMenuItem("menu.tools", CommandId.Copy);
            shell.AddMenuBarTrailing(() => new MenuExtraWidget("ProbeExtra", "probe"));
        }
    }
}
