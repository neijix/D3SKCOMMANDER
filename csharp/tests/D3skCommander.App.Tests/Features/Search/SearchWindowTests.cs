using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using D3skCommander.App;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Search;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Search;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Search;

public class SearchWindowTests
{
    private static Localizer English() =>
        Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    private static string Cfg() => SearchMemoryFolder.Fresh();

    private static SearchViewModel Model(
        SearchPanels panels,
        SearchPicker? picker = null,
        SearchEngine? engine = null,
        IDialogService? dialogs = null) =>
        new(English(), new JobService(), picker ?? new SearchPicker(null), panels, engine ?? new SearchEngine(), dialogs ?? new FakeDialogService(), new SearchHistoryStore(Cfg()), new SearchTemplateStore(Cfg()));

    private static async Task Finish(SearchViewModel model)
    {
        var stop = TestContext.Current.CancellationToken;
        await model.Job!.Completion.WaitAsync(TimeSpan.FromSeconds(10), stop);
        model.Drain();
    }

    [AvaloniaFact]
    public void ModuleRegistersTheCommandAndTheToolWindow()
    {
        var text = English();
        using var provider = TestComposition.Build(text);
        var module = provider.GetRequiredService<SearchModule>();
        var shell = new ShellContributions();
        module.Contribute(shell);
        Assert.True(shell.CanExecute(CommandId.Search));
    }

    [AvaloniaFact]
    public void TheCommandOpensTheWindowInTheActivePanelsFolder()
    {
        var panels = new SearchPanels("/work/here");
        var tools = new SearchTools();
        var model = Model(panels);
        var commands = new SearchCommands(panels, tools, model);
        var shell = new ShellContributions();
        commands.Bind(shell);
        Assert.Equal(ToolKind.Search, Assert.Single(tools.Registered));
        Assert.Same(model, tools.Factory!());
        shell.Dispatch(CommandId.Search);
        Assert.True(tools.Opened);
        Assert.Equal("/work/here", model.Roots);
    }

    [AvaloniaFact]
    public async Task AFormSearchStreamsItsResultsIntoTheSharedList()
    {
        using var dir = new SearchDir();
        dir.Write("a.txt", "needle in a line\n");
        dir.Write("sub/b.txt", "nothing\n");
        dir.Write("sub/c.md", "needle too\n");
        var model = Model(new SearchPanels(dir.Root));
        model.Prepare(dir.Root);
        model.NameMask = "*.txt";
        model.ContentText = "needle";
        var view = new SearchView(model, English());
        var window = new Window { Width = 820, Height = 620, Content = view };
        window.Show();
        try
        {
            model.StartCommand.Execute(null);
            await Finish(model);
            window.UpdateLayout();
            Assert.False(model.IsRunning);
            Assert.Equal(1, model.HitCount);
            Assert.Equal("a.txt", model.Results.CellText(0, 0));
            Assert.Equal(dir.Root, model.Results.CellText(0, 1));
            Assert.Equal("1: needle in a line", model.Results.CellText(0, 4));
            Assert.Equal("Done: 1 found", model.Status);
            Assert.Equal("Name", view.Results.HeaderText("name"));
            Assert.Equal("Folder", view.Results.HeaderText("search.col.folder"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CancelStopsARunningSearch()
    {
        var model = Model(new SearchPanels("/stall"), engine: new SearchEngine(new StallingProvider(), new StallingProvider()));
        model.Prepare("/stall");
        model.StartCommand.Execute(null);
        Assert.True(model.IsRunning);
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (model.HitCount == 0)
        {
            Assert.True(DateTime.UtcNow < until, "no result arrived");
            await Task.Delay(10, TestContext.Current.CancellationToken);
            model.Drain();
        }

        Assert.True(model.IsRunning);
        Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        await Finish(model);
        Assert.False(model.IsRunning);
        Assert.Equal("Cancelled: 1 found", model.Status);
        Assert.False(model.CancelCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task GoToFileOpensTheFolderInTheActivePanel()
    {
        using var dir = new SearchDir();
        var file = dir.Write("sub/target.txt", "x");
        var panels = new SearchPanels(dir.Root);
        var model = Model(panels);
        model.Prepare(dir.Root);
        model.NameMask = "target.txt";
        var view = new SearchView(model, English());
        var window = new Window { Width = 820, Height = 620, Content = view };
        window.Show();
        try
        {
            model.StartCommand.Execute(null);
            await Finish(model);
            window.UpdateLayout();
            Assert.Equal(1, model.HitCount);
            Assert.True(model.GoToFileCommand.CanExecute(null));
            await model.GoToFileCommand.ExecuteAsync(null);
            var visit = Assert.Single(panels.Visits);
            Assert.Equal(PanelSide.Right, visit.Side);
            Assert.Equal(Path.GetDirectoryName(file), visit.Path);

            view.Results.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (panels.Visits.Count < 2 && DateTime.UtcNow < until)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Equal(2, panels.Visits.Count);
            Assert.Equal(Path.GetDirectoryName(file), panels.Visits[1].Path);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BrowseFillsTheFolderAndABadFormExplainsItself()
    {
        var picker = new SearchPicker("/picked/folder");
        var model = Model(new SearchPanels("/start"), picker);
        model.Prepare("/start");
        await model.BrowseCommand.ExecuteAsync(null);
        Assert.Equal("/start", picker.StartedAt);
        Assert.Equal("/picked/folder", model.Roots);

        model.MinSize = "abc";
        model.StartCommand.Execute(null);
        Assert.Null(model.Job);
        Assert.False(model.IsRunning);
        Assert.Equal("Invalid size: abc", model.Status);
        model.MinSize = "";
        model.Roots = " ; ";
        model.StartCommand.Execute(null);
        Assert.Equal("No folder to search in", model.Status);
    }
}
