using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using D3skCommander.App;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.MultiRename;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.MultiRename;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Engine = D3skCommander.Core.MultiRename.MultiRename;

namespace D3skCommander.App.Tests.Features.MultiRename;

public class RenameWindowTests
{
    [AvaloniaFact]
    public void EveryTokenButtonExpands()
    {
        using var dir = new WorkDir();
        var folder = Path.Combine(dir.Root, "parent");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "report.txt");
        File.WriteAllText(file, "x");
        var text = Localizer.Load(Language.En, Language.En);
        var view = new RenameViewModel(text, new JobService());
        view.Load([file]);
        var (window, form) = Show(view, text);
        try
        {
            var buttons = Tokens(form);
            Assert.Equal(RenameToken.All.Count, buttons.Count);

            foreach (var token in RenameToken.All)
            {
                var label = text.Get(token.LabelKey);
                view.NameMask = "";
                Click(buttons.Single(b => (string?)b.Content == label));
                Assert.True(view.Session.Rule.NameMask == label, label + " inserted '" + view.Session.Rule.NameMask + "'");
                var expected = Engine.NewName(file, 0, view.Session.Rule);
                var preview = Assert.Single(view.Session.Previews);
                Assert.Null(preview.Error);
                Assert.Equal(expected, Path.GetFileName(preview.To));
                Assert.Equal(expected, view.Preview.CellText(0, 1));
                Assert.Equal("report.txt", view.Preview.CellText(0, 0));
                Assert.Equal(label, form.FindControl<TextBox>("MaskBox")!.Text);
            }


            Assert.Equal(text.Get("rename.col.old"), view.Preview.HeaderText("rename.col.old"));
            Assert.Equal(text.Get("rename.col.new"), view.Preview.HeaderText("rename.col.new"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TokenGoesInAtTheCaret()
    {
        using var dir = new WorkDir();
        var file = dir.Touch("report.txt");
        var text = Localizer.Load(Language.En, Language.En);
        var view = new RenameViewModel(text, new JobService());
        view.Load([file]);
        var (window, form) = Show(view, text);
        try
        {
            var box = form.FindControl<TextBox>("MaskBox")!;
            var label = text.Get(RenameToken.All[0].LabelKey);
            var button = Tokens(form).Single(b => (string?)b.Content == label);

            view.NameMask = "ab";
            box.CaretIndex = 1;
            Click(button);
            Assert.Equal("a" + label + "b", view.Session.Rule.NameMask);
            Assert.Equal("a" + label + "b", box.Text);
            Assert.Equal(1 + label.Length, box.CaretIndex);

            view.NameMask = "\U0001F600b";
            box.CaretIndex = 2;
            Click(button);
            Assert.Equal("\U0001F600" + label + "b", view.Session.Rule.NameMask);
            Assert.Equal(2 + label.Length, box.CaretIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PreviewErrorBlocksTheStartButton()
    {
        using var dir = new WorkDir();
        var first = dir.Touch("a.txt");
        var second = dir.Touch("b.txt");
        var jobs = new JobService();
        var view = new RenameViewModel(Localizer.Load(Language.En, Language.En), jobs);
        view.Load([first, second]);
        view.NameMask = "same";
        var (window, form) = Show(view, Localizer.Load(Language.En, Language.En));
        try
        {
            Assert.False(Named(form, "RunButton").IsEffectivelyEnabled);
            Assert.False(string.IsNullOrEmpty(view.Preview.CellText(0, 2)));
            Click(Named(form, "RunButton"));
            Assert.Empty(jobs.Jobs);
            Assert.Empty(view.Session.Done);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task StartThenUndoRenamesAndRestores()
    {
        using var dir = new WorkDir();
        var jobs = new JobService();
        var view = new RenameViewModel(Localizer.Load(Language.En, Language.En), jobs);
        var first = dir.Touch("a.txt");
        var second = dir.Touch("b.txt");
        view.Load([first, second]);
        var (window, form) = Show(view, Localizer.Load(Language.En, Language.En));
        try
        {
            view.NameMask = "n[C]";
            Click(Named(form, "RunButton"));
            var started = Assert.Single(jobs.Jobs);
            var applied = await started.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(JobOutcome.Completed, applied.Outcome);
            await Idle(view);
            Assert.True(File.Exists(dir.PathOf("n1.txt")));
            Assert.True(File.Exists(dir.PathOf("n2.txt")));
            Click(Named(form, "UndoButton"));
            Assert.Collection(jobs.Jobs, _ => { }, _ => { });
            var restored = await jobs.Jobs[1].Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(JobOutcome.Completed, restored.Outcome);
            await Idle(view);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.False(view.Session.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ModuleRegistersTheCommand()
    {
        var text = Localizer.Load(Language.En, Language.En);
        using var provider = TestComposition.Build(text);
        var module = provider.GetRequiredService<MultiRenameModule>();
        var shell = new ShellContributions();
        module.Contribute(shell);
        Assert.True(shell.CanExecute(CommandId.MultiRename));
    }

    [AvaloniaFact]
    public async Task NoSelectionShowsTheNoSelectionMessage()
    {
        var text = Localizer.Load(Language.En, Language.En);
        var dialogs = new FakeDialogService();
        var tools = new NoTools();
        var commands = new MultiRenameCommands(new NoSelection(), dialogs, text, tools, new RenameViewModel(text, new JobService()));
        var shell = new ShellContributions();
        commands.Bind(shell);
        shell.Dispatch(CommandId.MultiRename);
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(text.Get("dlg.no_selection"), shown.Text);
        Assert.False(tools.Opened);
        dialogs.Answer(DialogResult.Ok(null));
    }

    private sealed class NoTools : IToolWindowHost
    {
        public bool Opened { get; private set; }

        public void Register(ToolKind kind, Func<IShellWidget> content)
        {
        }

        public ToolWindow Open(ToolKind kind)
        {
            Opened = true;
            throw new NotSupportedException();
        }

        public bool IsOpen(ToolKind kind) => false;
    }

    private sealed class NoSelection : IPanelWorkspace
    {
        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) => throw new NotSupportedException();

        public PanelCapture? Capture() => null;

        public Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (Window Window, RenameView View) Show(RenameViewModel model, ILocalizer text)
    {
        var view = new RenameView(model, text);
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    private static List<Button> Tokens(RenameView view) =>
        view.GetVisualDescendants().OfType<Button>().Where(b => b.Parent is WrapPanel).ToList();

    private static Button Named(RenameView view, string name) =>
        view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Click(Button button)
    {
        if (!button.IsEffectivelyEnabled)
        {
            return;
        }

        if (button.Command is { } command)
        {
            command.Execute(null);
            return;
        }

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task Idle(RenameViewModel view)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (view.IsBusy)
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The rename job did not finish.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class WorkDir : IDisposable
    {
        public WorkDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "d3sk-rename-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Touch(string name)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, "x");
            return path;
        }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
