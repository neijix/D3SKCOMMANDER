using Avalonia.Headless.XUnit;
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

public class SearchDuplicatesTests
{
    private static async Task<(PanelWorkspace Panels, SearchViewModel Model)> Searched(SearchDir dir)
    {
        var panels = await Harness.Workspace(dir.Root, dir.Root);
        var model = SearchFakes.Model(panels);
        model.Prepare(dir.Root);
        model.FindDuplicates = true;
        model.StartCommand.Execute(null);
        await model.Job!.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        model.Drain();
        return (panels, model);
    }

    private static SearchDir TwoGroups()
    {
        var dir = new SearchDir();
        dir.Write("a.txt", "twelve bytes");
        dir.Write("one/b.txt", "twelve bytes");
        dir.Write("one/c.txt", "other");
        dir.Write("two/d.txt", "other");
        dir.Write("two/e.txt", "other");
        dir.Write("lonely.txt", "unique content");
        return dir;
    }

    [AvaloniaFact]
    public async Task ADuplicatesSearchShowsTheGroupColumnWithTheBiggestWasteFirst()
    {
        using var dir = TwoGroups();

        var (_, model) = await Searched(dir);

        var group = model.Results.ColumnIndex("search.col.group");
        Assert.True(group >= 0);
        var rows = Enumerable.Range(0, model.Hits.Count).Select(i => (model.Results.CellText(i, 0), model.Results.CellText(i, group))).ToList();
        Assert.Equal(
            [("a.txt", "1"), ("b.txt", "1"), ("c.txt", "2"), ("d.txt", "2"), ("e.txt", "2")],
            rows);
        Assert.Contains("2 groups", model.Status);
    }

    [AvaloniaFact]
    public async Task WithoutACriterionTheSearchDoesNotStartAndSaysSo()
    {
        using var dir = TwoGroups();
        var panels = await Harness.Workspace(dir.Root, dir.Root);
        var model = SearchFakes.Model(panels);
        model.Prepare(dir.Root);
        model.FindDuplicates = true;
        model.DupSize = false;
        model.DupContent = false;

        model.StartCommand.Execute(null);

        Assert.Null(model.Job);
        Assert.False(model.IsRunning);
        Assert.Contains("at least one duplicate criterion", model.Status);
    }

    [AvaloniaFact]
    public async Task FeedKeepsTheGroupsAndSelectAllButFirstMarksTheExtraCopies()
    {
        using var dir = TwoGroups();
        var (panels, model) = await Searched(dir);

        Assert.True(await model.FeedToPanelAsync());
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal(
            ["a.txt", $"one{sep}b.txt", $"one{sep}c.txt", $"two{sep}d.txt", $"two{sep}e.txt"],
            panels.Read(PanelSide.Left).Entries.Where(e => !e.IsParent).Select(e => e.Name).ToList());

        panels.SelectAllButFirst();

        var marked = panels.Read(PanelSide.Left).Marked.Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["b.txt", "d.txt", "e.txt"], marked);
    }

    [AvaloniaFact]
    public async Task SelectAllButFirstDoesNothingInAPlainFolder()
    {
        using var dir = TwoGroups();
        var panels = await Harness.Workspace(dir.Root, dir.Root);

        panels.SelectAllButFirst();

        Assert.Empty(panels.Read(PanelSide.Left).Marked);
    }
}
