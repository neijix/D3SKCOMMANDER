using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Favorites;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.Favorites;

public class FavoriteTabsCommandsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("d3sk-fav-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [AvaloniaFact]
    public async Task SavedTabsComeBackAfterTheyWereChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        var c = Directory.CreateDirectory(Path.Combine(_root, "c")).FullName;
        using var settings = new AppSettings(new SettingsFolder(Path.Combine(_root, "cfg"), SettingsSource.Env));
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var dialogs = new FakeDialogService();
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker(), null, settings);
        await workspace.OpenAsync(a, b, ct);
        await workspace.NewTabAtAsync(PanelSide.Left, c, ct);
        var commands = new FavoriteTabsCommands(workspace, settings, dialogs, text);

        dialogs.Enqueue(DialogResult.Ok("work"));
        await commands.SaveAsync();
        var saved = Assert.Single(settings.FavoriteTabs.Current.Sets);
        Assert.Equal(2, saved.Left.Tabs.Count);

        workspace.CloseTab();
        await workspace.GoAsync(PanelSide.Right, a, ct);
        Assert.Single(workspace.CaptureFavorites("x")!.Left.Tabs);

        await commands.RestoreAsync("work");

        var after = workspace.CaptureFavorites("work")!;
        Assert.Equal(saved.Left.Tabs.Select(t => t.Directory), after.Left.Tabs.Select(t => t.Directory));
        Assert.Equal(saved.Left.ActiveIndex, after.Left.ActiveIndex);
        Assert.Equal(saved.Right.Tabs.Select(t => t.Directory), after.Right.Tabs.Select(t => t.Directory));
        Assert.Equal(c, workspace.Read(PanelSide.Left).Path);
        Assert.Equal(b, workspace.Read(PanelSide.Right).Path);
    }

    [AvaloniaFact]
    public async Task TheShowMenuListsTheSavedSetAndSaveAndManage()
    {
        var ct = TestContext.Current.CancellationToken;
        using var settings = new AppSettings(new SettingsFolder(Path.Combine(_root, "cfg"), SettingsSource.Env));
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var dialogs = new FakeDialogService();
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker(), null, settings);
        await workspace.OpenAsync(_root, _root, ct);
        var commands = new FavoriteTabsCommands(workspace, settings, dialogs, text);
        var shell = new ShellContributions();

        var empty = Assert.Single(commands.MenuItems(shell));
        Assert.Equal(["Save current tabs…", "Manage favorite tabs…"], empty.Children!.Select(i => i.Header));

        dialogs.Enqueue(DialogResult.Ok("work"));
        await commands.SaveAsync();

        var children = Assert.Single(commands.MenuItems(shell)).Children!;
        Assert.Equal("work", children[0].Header);
        Assert.True(children[1].IsRule);
        Assert.Equal(4, children.Count);
    }

    [AvaloniaFact]
    public void TheEditorRenamesDeletesAndMovesTheDraft()
    {
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var saved = new D3skCommander.Core.Panels.FavoriteTabs();
        foreach (var name in new[] { "a", "b", "c" })
        {
            saved.Save(new D3skCommander.Core.Panels.FavoriteTabSet { Name = name });
        }

        var model = new D3skCommander.App.Features.Settings.FavoriteTabsEditorModel(text, new FakeDialogService(), saved, D3skCommander.Core.Panels.FavoriteRestoreMode.Add);
        Assert.Equal(1, model.ModeIndex);
        Assert.False(model.Changed);

        model.SelectedRow = model.Rows[0];
        model.MoveDownCommand.Execute(null);
        Assert.True(model.Rename("c", "z"));
        model.SelectedRow = model.Rows[0];
        model.DeleteCommand.Execute(null);

        Assert.True(model.Changed);
        Assert.Equal(["a", "z"], model.Result().Sets.Select(x => x.Name));
        Assert.Equal(3, saved.Sets.Count);
    }

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
