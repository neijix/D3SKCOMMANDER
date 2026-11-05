using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Settings;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

public class SettingsFolderTests
{
    [Fact]
    public async Task MovingTheFolderMovesTheFilesAndWritesThePointer()
    {
        using var harness = new SettingsHarness();
        var stop = TestContext.Current.CancellationToken;
        File.WriteAllText(Path.Combine(harness.Folder, SettingsFileNames.Preferences), "{}");
        var target = Path.Combine(harness.Root, "elsewhere");
        var commands = harness.NewFolderCommands();

        var running = commands.ChangeAsync();
        var form = await harness.Dialogs.WaitShownAsync();
        var field = Assert.IsType<FolderDialogField>(form.Form!.Fields[1]);
        Assert.Equal(harness.Folder, field.Path);
        field.Path = target;
        harness.Dialogs.Answer(DialogResult.Ok());
        var done = await harness.Dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, done.Kind);
        Assert.Contains(target, done.Text, StringComparison.Ordinal);
        Assert.Contains("restart", done.Text, StringComparison.Ordinal);
        harness.Dialogs.Answer(DialogResult.Ok());

        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(10), stop));
        Assert.Equal(target, SettingsDirectory.ReadPointer(harness.Install));
        Assert.True(File.Exists(Path.Combine(target, SettingsFileNames.Preferences)));
        Assert.False(File.Exists(Path.Combine(harness.Folder, SettingsFileNames.Preferences)));
    }

    [Fact]
    public async Task ACancelledQuestionChangesNothing()
    {
        using var harness = new SettingsHarness();
        var stop = TestContext.Current.CancellationToken;
        var running = harness.NewFolderCommands().ChangeAsync();
        await harness.Dialogs.WaitShownAsync();
        harness.Dialogs.Answer(DialogResult.Cancel());

        Assert.False(await running.WaitAsync(TimeSpan.FromSeconds(10), stop));
        Assert.Null(SettingsDirectory.ReadPointer(harness.Install));
    }

    [Fact]
    public async Task WithTheConfigVariableTheFolderIsFixed()
    {
        using var harness = new SettingsHarness(SettingsSource.Env);
        var stop = TestContext.Current.CancellationToken;
        var running = harness.NewFolderCommands().ChangeAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, shown.Kind);
        Assert.Equal(harness.Text.Get("settings.folder_env"), shown.Text);
        harness.Dialogs.Answer(DialogResult.Ok());

        Assert.False(await running.WaitAsync(TimeSpan.FromSeconds(10), stop));
        Assert.Null(SettingsDirectory.ReadPointer(harness.Install));
    }

    [Fact]
    public async Task TheFirstLaunchQuestionIsAskedOnceWithTheProposal()
    {
        using var harness = new SettingsHarness(SettingsSource.Default);
        var stop = TestContext.Current.CancellationToken;
        Assert.True(SettingsFolderMove.ShouldAskFirstLaunch(harness.Settings.Folder, screenshotHookActive: false));
        var commands = harness.NewFolderCommands();

        var running = commands.AskFirstLaunchAsync();
        var form = await harness.Dialogs.WaitShownAsync();
        var field = Assert.IsType<FolderDialogField>(form.Form!.Fields[1]);
        Assert.Equal(Path.Combine(harness.Root, "install", SettingsFileNames.Subfolder), field.Path);
        field.Path = harness.Folder;
        harness.Dialogs.Answer(DialogResult.Ok());

        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(10), stop));
        Assert.True(File.Exists(Path.Combine(harness.Folder, SettingsFileNames.Preferences)));
        Assert.False(SettingsFolderMove.ShouldAskFirstLaunch(harness.Settings.Folder, screenshotHookActive: false));
        Assert.False(await commands.AskFirstLaunchAsync().WaitAsync(TimeSpan.FromSeconds(10), stop));
        Assert.Equal(1, harness.Dialogs.ShownCount);
    }

    [Fact]
    public async Task FirstLaunchQuestionIsSkippedUnderTheScreenshotHook()
    {
        using var harness = new SettingsHarness(SettingsSource.Default);
        var stop = TestContext.Current.CancellationToken;

        Assert.False(SettingsFolderMove.ShouldAskFirstLaunch(harness.Settings.Folder, screenshotHookActive: true));
        Assert.False(await harness.NewFolderCommands(screenshotActive: true).AskFirstLaunchAsync().WaitAsync(TimeSpan.FromSeconds(10), stop));
        Assert.Equal(0, harness.Dialogs.ShownCount);
    }

    [Fact]
    public void TheFirstLaunchQuestionIsNeverAskedWithTheConfigVariableOrExistingSettings()
    {
        var withVariable = new SettingsFolder(Path.Combine(Path.GetTempPath(), "d3sk-never-existing"), SettingsSource.Env);
        Assert.False(SettingsFolderMove.ShouldAskFirstLaunch(withVariable, screenshotHookActive: false));
        var chosen = new SettingsFolder(Path.Combine(Path.GetTempPath(), "d3sk-never-existing"), SettingsSource.Chosen);
        Assert.False(SettingsFolderMove.ShouldAskFirstLaunch(chosen, screenshotHookActive: false));
    }
}
