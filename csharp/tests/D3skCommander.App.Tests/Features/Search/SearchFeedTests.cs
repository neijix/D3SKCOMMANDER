using Avalonia.Headless.XUnit;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Search;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Search;
using Xunit;
using Harness = D3skCommander.App.Tests.Features.FileOps.FileOpsCommandTests;

namespace D3skCommander.App.Tests.Features.Search;

public class SearchFeedTests
{
    private static Localizer English() =>
        Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    private static async Task<(PanelWorkspace Panels, SearchViewModel Model)> Searched(SearchDir dir)
    {
        var panels = await Harness.Workspace(dir.Root, dir.Root);
        var model = new SearchViewModel(
            English(),
            new JobService(),
            new SearchPicker(null),
            panels,
            new SearchEngine(),
            new FakeDialogService(),
            new SearchHistoryStore(SearchMemoryFolder.Fresh()),
            new SearchTemplateStore(SearchMemoryFolder.Fresh()));
        model.Prepare(dir.Root);
        model.NameMask = "*.txt";
        model.StartCommand.Execute(null);
        await model.Job!.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        model.Drain();
        return (panels, model);
    }

    private static List<string> Names(PanelWorkspace panels) =>
        panels.Read(PanelSide.Left).Entries.Where(entry => !entry.IsParent).Select(entry => entry.Name).Order().ToList();

    [AvaloniaFact]
    public async Task FeedOpensANewTabListingTheHitsAndAFolderHitEntersTheRealFolder()
    {
        using var dir = new SearchDir();
        dir.Write("a.txt", "1");
        dir.Write("one/same.txt", "2");
        dir.Write("two/same.txt", "3");
        var (panels, model) = await Searched(dir);
        Assert.True(model.FeedCommand.CanExecute(null));

        Assert.True(await model.FeedToPanelAsync());

        var snap = panels.Read(PanelSide.Left);
        Assert.Equal(Path.Combine(dir.Root, "Search: *.txt"), snap.Path);
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal(["a.txt", $"one{sep}same.txt", $"two{sep}same.txt"], Names(panels));
        Assert.Equal(dir.Root, snap.Entries.Single(entry => entry.IsParent).Path);

        await panels.ParentAsync(TestContext.Current.CancellationToken);
        Assert.Equal(dir.Root, panels.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task DeletingTheHitUnderTheCursorRemovesTheRealFileAndTheRow()
    {
        using var dir = new SearchDir();
        var a = dir.Write("a.txt", "1");
        dir.Write("one/b.txt", "2");
        var (panels, model) = await Searched(dir);
        await model.FeedToPanelAsync();
        var snap = panels.Read(PanelSide.Left);
        panels.ApplySelection(PanelSide.Left, Harness.At(snap, "a.txt"), []);
        var dialogs = new Harness.ScriptedDialogs { Confirm = true };
        var commands = Harness.Commands(panels, dialogs, settings: Harness.Settings(useTrash: false, confirm: true));

        await commands.DeleteAsync();

        Assert.False(File.Exists(a));
        Assert.Equal([Path.Combine("one", "b.txt")], Names(panels));
    }

    [AvaloniaFact]
    public async Task GoToTheFilesFolderLeavesTheResultsAndPutsTheCursorOnTheFile()
    {
        using var dir = new SearchDir();
        dir.Write("a.txt", "1");
        var b = dir.Write("one/b.txt", "2");
        var (panels, model) = await Searched(dir);
        await model.FeedToPanelAsync();
        var snap = panels.Read(PanelSide.Left);
        panels.ApplySelection(PanelSide.Left, Harness.At(snap, Path.Combine("one", "b.txt")), []);

        await panels.GoToContainingFolderAsync(TestContext.Current.CancellationToken);

        var now = panels.Read(PanelSide.Left);
        Assert.Equal(Path.GetDirectoryName(b), now.Path);
        Assert.Equal(b, now.Entries[now.Cursor].Path);
    }

    [AvaloniaFact]
    public async Task GoToTheFilesFolderInAPlainFolderKeepsTheFolder()
    {
        using var dir = new SearchDir();
        var a = dir.Write("a.txt", "1");
        var panels = await Harness.Workspace(dir.Root, dir.Root);
        var snap = panels.Read(PanelSide.Left);
        panels.ApplySelection(PanelSide.Left, Harness.At(snap, "a.txt"), []);

        await panels.GoToContainingFolderAsync(TestContext.Current.CancellationToken);

        var now = panels.Read(PanelSide.Left);
        Assert.Equal(dir.Root, now.Path);
        Assert.Equal(a, now.Entries[now.Cursor].Path);
    }

    [AvaloniaFact]
    public async Task RefreshDropsAHitThatWasDeletedOutsideTheApp()
    {
        using var dir = new SearchDir();
        var a = dir.Write("a.txt", "1");
        dir.Write("b.txt", "2");
        var (panels, model) = await Searched(dir);
        await model.FeedToPanelAsync();
        File.Delete(a);

        await panels.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["b.txt"], Names(panels));
    }
}
