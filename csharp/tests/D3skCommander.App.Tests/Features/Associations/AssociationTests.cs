using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Associations;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Features.Launch;
using D3skCommander.App.Tests.Features.Settings;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Associations;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.Associations;

public class AssociationTests
{
    [AvaloniaFact]
    public async Task EnterOnAMatchingFileLaunchesTheConfiguredProgramWithTheExpandedArguments()
    {
        using var rig = await Rig.Open(new AssociationRule("Pictures", "*.PNG", new AssociationAction("/opt/my viewer/v", "--file \"%p\" --name %f")));
        await rig.Launch.AddFileAsync("a b.png");
        rig.Launch.Select("a b.png");

        await rig.Pane.OpenEntryAsync();

        var request = Assert.IsType<ProcessRequest.Argv>(await rig.WaitStartedAsync(1));
        var path = Path.Combine(rig.Launch.Left, "a b.png");
        Assert.Equal("/opt/my viewer/v", request.Program);
        Assert.Equal(["--file", path, "--name", "a b.png"], request.Arguments);
        Assert.Equal(rig.Launch.Left, request.WorkingDirectory);
    }

    [AvaloniaFact]
    public async Task AFileNoRuleMatchesIsStillOpenedWithTheSystemDefault()
    {
        using var rig = await Rig.Open(new AssociationRule("Pictures", "*.png", new AssociationAction("viewer")));
        await rig.Launch.AddFileAsync("notes.txt");
        rig.Launch.Select("notes.txt");

        await rig.Pane.OpenEntryAsync();

        var path = Path.Combine(rig.Launch.Left, "notes.txt");
        var request = Assert.IsType<ProcessRequest.Argv>(await rig.WaitStartedAsync(1));
        var expected = Assert.IsType<ProcessRequest.Argv>(Launcher.DefaultOpenRequest(path, LaunchPlatforms.Current));
        Assert.Equal(expected.Program, request.Program);
        Assert.Equal(expected.Arguments, request.Arguments);
    }

    [AvaloniaFact]
    public async Task AnArchiveWithAnOpenRuleIsOpenedByTheRuleAndWithoutOneIsEntered()
    {
        using var rig = await Rig.Open(new AssociationRule("Zips", "a*.zip", new AssociationAction("unpacker")));
        await rig.Launch.AddFileAsync("a.zip");
        await rig.Launch.AddFileAsync("b.zip");
        rig.Launch.Select("a.zip");
        Assert.Equal(Path.Combine(rig.Launch.Left, "a.zip"), rig.Launch.Workspace.FileToOpen(PanelSide.Left));

        rig.Launch.Select("b.zip");
        Assert.Null(rig.Launch.Workspace.FileToOpen(PanelSide.Left));
    }

    [AvaloniaFact]
    public async Task F3WithAViewRuleLaunchesItInsteadOfTheViewer()
    {
        using var rig = await Rig.Open(new AssociationRule("Logs", "*.log", view: new AssociationAction("pager", "-R")));
        await rig.Launch.AddFileAsync("x.log");
        rig.Launch.Select("x.log");
        var viewer = new ViewerCommands(rig.Launch.Workspace, new NoTools(), rig.Launch.Dialogs, rig.Launch.Text, new JobService(), rig.Service);

        await viewer.ViewAsync();

        var request = Assert.IsType<ProcessRequest.Argv>(await rig.WaitStartedAsync(1));
        Assert.Equal("pager", request.Program);
        Assert.Equal(["-R", Path.Combine(rig.Launch.Left, "x.log")], request.Arguments);
    }

    [AvaloniaFact]
    public async Task F3WithoutAViewRuleKeepsTheInternalViewer()
    {
        using var rig = await Rig.Open(new AssociationRule("Logs", "*.log", new AssociationAction("opener")));
        await rig.Launch.AddFileAsync("x.log");
        rig.Launch.Select("x.log");
        var tools = new NoTools();
        var viewer = new ViewerCommands(rig.Launch.Workspace, tools, rig.Launch.Dialogs, rig.Launch.Text, new JobService(), rig.Service);

        Assert.Throws<InvalidOperationException>(() => { _ = viewer.ViewAsync(); });

        Assert.Equal([ToolKind.Lister], tools.Opened);
        Assert.Empty(rig.Launch.Runner.Started);
    }

    [AvaloniaFact]
    public async Task F4WithAnEditRuleLaunchesItInsteadOfTheEditor()
    {
        using var rig = await Rig.Open(new AssociationRule("Sources", "*.cs", edit: new AssociationAction("code", "-g %p:1")));
        await rig.Launch.AddFileAsync("x.cs");
        rig.Launch.Select("x.cs");
        var editor = new EditorCommands(rig.Launch.Workspace, new NoTools(), rig.Launch.Dialogs, rig.Launch.Text, rig.Service);

        await editor.EditAsync();

        var request = Assert.IsType<ProcessRequest.Argv>(await rig.WaitStartedAsync(1));
        Assert.Equal("code", request.Program);
        Assert.Equal(["-g", Path.Combine(rig.Launch.Left, "x.cs") + ":1"], request.Arguments);
    }

    [AvaloniaFact]
    public async Task TheContextMenuListsTheProgramsAndTheExtraActionsAndRunningOneLaunchesIt()
    {
        var rule = new AssociationRule(
            "Pictures",
            "*.png",
            new AssociationAction("/usr/bin/viewer.exe"),
            edit: new AssociationAction("painter"),
            extras: [new AssociationAction("optipng", "-o7 %f", Name: "Optimize")]);
        using var rig = await Rig.Open(rule);
        await rig.Launch.AddFileAsync("a.png");
        await rig.Launch.AddFileAsync("b.txt");
        rig.Launch.Select("a.png");

        var menu = rig.Pane.FileMenu();

        Assert.Equal(["Open with viewer", "Edit with painter", "", "Optimize"], menu.Select(item => item.Header));
        Assert.True(menu[2].IsRule);
        menu[3].Invoke();
        var request = Assert.IsType<ProcessRequest.Argv>(await rig.WaitStartedAsync(1));
        Assert.Equal("optipng", request.Program);
        Assert.Equal(["-o7", "a.png"], request.Arguments);
        Assert.Equal(rig.Launch.Left, request.WorkingDirectory);

        rig.Launch.Select("b.txt");
        Assert.Empty(rig.Pane.FileMenu());
    }

    [AvaloniaFact]
    public async Task AFailingProgramShowsAMessageWithTheError()
    {
        using var rig = await Rig.Open(new AssociationRule("Pictures", "*.png", new AssociationAction("nope")));
        rig.Launch.Runner.FailWith = new InvalidOperationException("no such program");
        await rig.Launch.AddFileAsync("a.png");
        rig.Launch.Select("a.png");

        var pending = rig.Service.TryStart(AssociationKind.Open, Path.Combine(rig.Launch.Left, "a.png"));

        var shown = await rig.Launch.Dialogs.WaitShownAsync();
        Assert.Contains("no such program", shown.Text);
        rig.Launch.Dialogs.Answer(DialogResult.Ok());
        await pending!;
    }

    [AvaloniaFact]
    public void TheSettingsEditorAddsARuleAndSavingPersistsIt()
    {
        using var harness = new SettingsHarness();
        var model = harness.NewView();
        Assert.True(model.Associations.IsEmpty);

        var row = model.Associations.AddRule("Pictures", "*.png;*.jpg");
        row.Open.Program = "viewer";
        row.Open.Arguments = "\"%p\"";
        row.View.Program = "pager";
        row.AddExtra();
        row.SelectedExtra!.Program = "optipng";
        row.SelectedExtra.Arguments = "%f";
        model.Associations.AddRule("No masks", "");
        model.Associations.AddRule("Second", "*.md").Edit.Program = "ed";
        model.Associations.SelectedRow = row;
        model.Associations.MoveDownCommand.Execute(null);
        model.Associations.MoveUpCommand.Execute(null);
        model.Apply();
        harness.Settings.Flush();

        var rules = harness.Settings.Preferences.Current.Associations;
        Assert.Equal(["Pictures", "Second"], rules.Select(rule => rule.Name));
        Assert.Equal(new AssociationAction("viewer", "\"%p\""), rules[0].Open);
        Assert.Equal("pager", rules[0].View!.Program);
        Assert.Equal([new AssociationAction("optipng", "%f", Name: "New action")], rules[0].Extras);
        using var reread = new PreferencesStore(harness.Folder);
        Assert.Equal(["Pictures", "Second"], reread.Current.Associations.Select(rule => rule.Name));
        Assert.Equal("ed", reread.Current.Associations[1].Edit!.Program);

        var again = harness.NewView();
        Assert.Equal(2, again.Associations.Rows.Count);
        again.Associations.SelectedRow = again.Associations.Rows[1];
        again.Associations.RemoveCommand.Execute(null);
        again.Apply();
        Assert.Single(harness.Settings.Preferences.Current.Associations);
    }

    [AvaloniaFact]
    public void TheSettingsApplyKeepsTheRulesWhenTheEditorIsLeftAlone()
    {
        using var harness = new SettingsHarness();
        harness.Settings.Preferences.Save(new Preferences { Associations = [new AssociationRule("T", "*.txt", new AssociationAction("ed"))] });

        var model = harness.NewView();
        model.Apply();

        Assert.Equal("ed", harness.Settings.Preferences.Current.Associations.Single().Open!.Program);
    }

    private sealed class NoTools : IToolWindowHost
    {
        public List<ToolKind> Opened { get; } = [];

        public void Register(ToolKind kind, Func<IShellWidget> content)
        {
        }

        public ToolWindow Open(ToolKind kind)
        {
            Opened.Add(kind);
            throw new InvalidOperationException("The test host opens no window.");
        }

        public bool IsOpen(ToolKind kind) => false;
    }

    private sealed class Rig : IDisposable
    {
        private Rig(LaunchHarness launch, FileAssociationService service, PanelPaneModel pane)
        {
            Launch = launch;
            Service = service;
            Pane = pane;
        }

        public LaunchHarness Launch { get; }

        public FileAssociationService Service { get; }

        public PanelPaneModel Pane { get; }

        public static async Task<Rig> Open(params AssociationRule[] rules)
        {
            var launch = await LaunchHarness.Open();
            launch.Settings.Preferences.Save(new Preferences { Associations = [.. rules] });
            var service = new FileAssociationService(launch.Settings, launch.Commands, launch.Text);
            var bar = new CommandLineBarWidget(launch.Workspace, launch.Runner, launch.Dialogs, launch.Text);
            new LaunchModule(launch.Commands, bar, launch.Workspace, service).Contribute(new ShellContributions());
            var pane = new PanelPaneModel(PanelSide.Left, launch.Workspace, launch.Text, launch.Dialogs);
            return new Rig(launch, service, pane);
        }

        public async Task<ProcessRequest> WaitStartedAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Launch.Runner.Started.Count < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Equal(count, Launch.Runner.Started.Count);
            return Launch.Runner.Started[count - 1];
        }

        public void Dispose() => Launch.Dispose();
    }
}
