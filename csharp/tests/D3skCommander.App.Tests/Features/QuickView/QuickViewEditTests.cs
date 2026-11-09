using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickView;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Smoke;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.TextDocument;
using Xunit;

namespace D3skCommander.App.Tests.Features.QuickView;

public class QuickViewEditTests
{
    [AvaloniaFact]
    public async Task ATextFileOpensInTheEditorWithItsMenu()
    {
        using var rig = await Rig.Open();

        var view = rig.Commands.Panel!.ShownEditor!;
        Assert.Equal("alpha\n", rig.Commands.Panel.ShownText);
        Assert.True(rig.Commands.Edited!.CanEdit);
        Assert.Equal(["_File", "_Edit", "_Search", "_View"], view.MenuEntries.Select(entry => (string)entry.Header!));
    }

    [AvaloniaFact]
    public async Task TheLineCommandsChangeTheText()
    {
        using var rig = await Rig.Open();
        var view = rig.Commands.Panel!.ShownEditor!;

        view.Change(LineEdits.Duplicate);

        Assert.Equal("alpha\nalpha\n", rig.Commands.Edited!.Buffer.Text);
        Assert.True(rig.Commands.Edited.Dirty);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MovingOffAChangedFileAsksSaveDontSaveOrCancel(int pick)
    {
        using var rig = await Rig.Open();
        var edited = rig.Commands.Edited!;
        edited.Buffer.Insert(0, "X");

        rig.RestOn("b.txt");
        var shown = await rig.Dialogs.WaitShownAsync();
        Assert.Equal(["Save", "Don't save", "Cancel"], shown.Choices);
        rig.Dialogs.Answer(DialogResult.Picked(pick));
        await rig.WaitFor(() => pick == 2 || rig.Commands.Panel!.ShownText == "beta\n");

        Assert.Equal(pick == 2 ? "Xalpha\n" : "beta\n", rig.Commands.Panel!.ShownText);
        Assert.Equal(pick == 0 ? "Xalpha\n" : "alpha\n", File.ReadAllText(Path.Combine(rig.Root, "a.txt")));

        // A cancel keeps the text and does not ask again until the cursor moves.
        rig.Commands.Tick();
        rig.Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        rig.Commands.Tick();
        Assert.Equal(1, rig.Dialogs.ShownCount);
    }

    [AvaloniaFact]
    public async Task QuittingWithAChangeAsksAndACancelKeepsTheApp()
    {
        using var rig = await Rig.Open();
        rig.Commands.Edited!.Buffer.Insert(0, "X");

        var quit = rig.Commands.SettleAsync();
        await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(DialogResult.Picked(2));

        Assert.False(await quit);
        using var clean = await Rig.Clean();
        Assert.True(await clean.Commands.SettleAsync());
    }

    [AvaloniaFact]
    public async Task ClosingTheViewWithAChangeAsksFirst()
    {
        using var rig = await Rig.Open();
        rig.Commands.Edited!.Buffer.Insert(0, "X");

        rig.Commands.Toggle();
        await rig.Dialogs.WaitShownAsync();
        Assert.True(rig.Commands.IsOn);
        rig.Dialogs.Answer(DialogResult.Picked(1));
        await rig.WaitFor(() => !rig.Commands.IsOn);

        Assert.False(rig.Commands.IsOn);
    }

    private sealed class Rig : IDisposable
    {
        private Rig(string root, PanelWorkspace workspace)
        {
            Root = root;
            Workspace = workspace;
            Clock = new FakeTimeProvider();
            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            Commands = new QuickViewCommands(workspace, text, new JobService(), Clock, Dialogs);
        }

        public string Root { get; }

        public PanelWorkspace Workspace { get; }

        public FakeTimeProvider Clock { get; }

        public FakeDialogService Dialogs { get; } = new();

        public QuickViewCommands Commands { get; }

        /// <summary>The quick view on, showing a.txt in the editor.</summary>
        public static async Task<Rig> Open()
        {
            var rig = await Clean();
            rig.Commands.Toggle();
            await rig.WaitFor(() => rig.Commands.Edited is not null);
            Assert.NotNull(rig.Commands.Edited);
            return rig;
        }

        public static async Task<Rig> Clean()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-quick-edit-").FullName;
            File.WriteAllText(Path.Combine(root, "a.txt"), "alpha\n");
            File.WriteAllText(Path.Combine(root, "b.txt"), "beta\n");
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new FileOps.FileOpsCommandTests.IdlePicker());
            await workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            workspace.Activate(PanelSide.Left);
            var rig = new Rig(root, workspace);
            rig.MoveCursorTo("a.txt");
            return rig;
        }

        public void MoveCursorTo(string name)
        {
            var snap = Workspace.Read(PanelSide.Left);
            var index = snap.Entries.ToList().FindIndex(entry => entry.Name == name);
            Workspace.ApplySelection(PanelSide.Left, index, []);
        }

        public void RestOn(string name)
        {
            MoveCursorTo(name);
            Commands.Tick();
            Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
            Commands.Tick();
        }

        public async Task WaitFor(Func<bool> done)
        {
            for (var i = 0; i < 300 && !done(); i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }

        public void Dispose()
        {
            Commands.Stop();
            Workspace.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
