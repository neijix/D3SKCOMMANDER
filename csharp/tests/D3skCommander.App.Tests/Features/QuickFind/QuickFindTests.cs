using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickFind;
using D3skCommander.App.Features.Search;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Features.Search;
using D3skCommander.App.Tests.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Index;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Harness = D3skCommander.App.Tests.Features.FileOps.FileOpsCommandTests;

namespace D3skCommander.App.Tests.Features.QuickFind;

public class QuickFindTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private static ILocalizer English() => Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    private static async Task<IndexService> Indexed(SearchDir dir)
    {
        var service = new IndexService(
            Path.Combine(dir.Root, "idx", "names.bin"),
            () => new IndexOptions([Path.Combine(dir.Root, "tree")], []));
        await service.BuildAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        return service;
    }

    private static QuickFindViewModel Model(IndexService index, IPanelWorkspace panels) =>
        new(English(), panels, index) { Debounce = TimeSpan.Zero };

    private static void Tree(SearchDir dir)
    {
        dir.Write("tree/report.txt", "1");
        dir.Write("tree/notes/report-final.txt", "22");
        dir.Write("tree/notes/photo.jpg", "333");
        dir.Write("tree/other.md", "4");
    }

    [AvaloniaFact]
    public async Task TypingFiltersTheListAndTheStatusSaysHowManyMatched()
    {
        using var dir = new SearchDir();
        Tree(dir);
        using var index = await Indexed(dir);
        var model = Model(index, await Harness.Workspace(dir.Root, dir.Root));

        model.QueryText = "report";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(["report.txt", "report-final.txt"], model.Rows.Select(row => row.Name).ToArray());
        Assert.Equal(2, model.Total);
        Assert.Contains("2 of 2 matches", model.Status, StringComparison.Ordinal);
        Assert.Same(model.Rows[0], model.Selected);

        model.QueryText = "ext:jpg";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(["photo.jpg"], model.Rows.Select(row => row.Name).ToArray());

        model.QueryText = "";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Empty(model.Rows);
        Assert.StartsWith("Index from ", model.Status, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ANewerQueryReplacesAnOlderOneStillWaiting()
    {
        using var dir = new SearchDir();
        Tree(dir);
        using var index = await Indexed(dir);
        var model = Model(index, await Harness.Workspace(dir.Root, dir.Root));
        model.Debounce = TimeSpan.FromMilliseconds(150);

        model.QueryText = "photo";
        var older = model.SearchTask;
        model.QueryText = "other";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await older.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(["other.md"], model.Rows.Select(row => row.Name).ToArray());
    }

    [AvaloniaFact]
    public async Task EnterRevealsTheSelectedFileInThePanel()
    {
        using var dir = new SearchDir();
        Tree(dir);
        using var index = await Indexed(dir);
        var panels = await Harness.Workspace(dir.Root, dir.Root);
        var model = Model(index, panels);
        model.QueryText = "photo";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.True(await model.RevealSelectedAsync());

        var snap = panels.Read(PanelSide.Left);
        Assert.Equal(Path.Combine(dir.Root, "tree", "notes"), snap.Path);
        Assert.Equal("photo.jpg", snap.Entries[snap.Cursor].Name);
    }

    [AvaloniaFact]
    public async Task AFileDeletedSinceIndexingSaysSoAndLeavesTheList()
    {
        using var dir = new SearchDir();
        Tree(dir);
        using var index = await Indexed(dir);
        var panels = await Harness.Workspace(dir.Root, dir.Root);
        var model = Model(index, panels);
        model.QueryText = "report";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(dir.Root, "tree", "report.txt"));

        Assert.False(await model.RevealSelectedAsync());

        Assert.Equal(["report-final.txt"], model.Rows.Select(row => row.Name).ToArray());
        Assert.Equal("No longer exists: report.txt", model.Status);
        Assert.Equal("report-final.txt", model.Selected!.Name);
        Assert.Equal(dir.Root, panels.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task FeedToPanelOpensATabListingTheHits()
    {
        using var dir = new SearchDir();
        Tree(dir);
        using var index = await Indexed(dir);
        var panels = await Harness.Workspace(dir.Root, dir.Root);
        var model = Model(index, panels);
        Assert.False(await model.FeedToPanelAsync());
        model.QueryText = "report";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.True(await model.FeedToPanelAsync());

        var snap = panels.Read(PanelSide.Left);
        // The tab folder name drops characters the file system forbids (':' on Windows).
        Assert.Equal(Path.Combine(dir.Root, OperatingSystem.IsWindows() ? "Quick find_ report" : "Quick find: report"), snap.Path);
        var sep = Path.DirectorySeparatorChar;
        var names = snap.Entries.Where(entry => !entry.IsParent).Select(entry => entry.Name).Order().ToArray();
        Assert.Equal(2, names.Length);
        Assert.EndsWith($"notes{sep}report-final.txt", names[0], StringComparison.Ordinal);
        Assert.EndsWith($"tree{sep}report.txt", names[1], StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task OpeningWithNoIndexStartsABuildAndShowsIt()
    {
        using var dir = new SearchDir();
        Tree(dir);
        using var index = new IndexService(
            Path.Combine(dir.Root, "idx", "names.bin"),
            () => new IndexOptions([Path.Combine(dir.Root, "tree")], []));
        var model = Model(index, await Harness.Workspace(dir.Root, dir.Root));
        Assert.Equal(English().Get("quickfind.no_index"), model.Status);

        await model.OpenedAsync();
        Assert.True(index.IsBuilding || index.Current is not null);
        await index.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.True(index.HasFile);
    }

    [AvaloniaFact]
    public async Task TheCommandOpensTheWindowAndTypingInTheBoxListsHits()
    {
        using var dir = new SearchDir();
        Tree(dir);
        var index = await Indexed(dir);
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton(index));
        host.Window.Dispatch(CommandId.QuickFind);
        var model = host.Provider.GetRequiredService<QuickFindViewModel>();
        model.Debounce = TimeSpan.Zero;
        var tool = host.Provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.QuickFind);
        var box = tool.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "QueryBox");
        var list = tool.GetVisualDescendants().OfType<ListBox>().Single(item => item.Name == "ResultList");

        box.Text = "report";
        await model.SearchTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(2, list.ItemCount);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
        Assert.Equal("report-final.txt", model.Selected!.Name);
        var feed = tool.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "FeedButton");
        Assert.True(feed.IsVisible);
        var rebuild = tool.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "RebuildButton");
        Assert.NotNull(rebuild.Content);
        rebuild.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await index.Completion.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }
}
