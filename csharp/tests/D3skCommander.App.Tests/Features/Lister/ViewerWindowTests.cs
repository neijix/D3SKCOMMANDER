using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.ToolWindows;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Viewer;
using D3skCommander.Tests.Shared;
using Xunit;

namespace D3skCommander.App.Tests.Features.Lister;

public class ViewerWindowTests
{
    [AvaloniaFact]
    public async Task F3OpensTheSelectedFileInTextThenHex()
    {
        using var rig = await Rig.Open(("notes.txt", "hello\nworld\n"));
        rig.Select("notes.txt");

        var tool = await rig.ViewAsync();

        var model = Assert.IsType<ViewerViewModel>(tool.View);
        Assert.Equal(ToolKind.Lister, rig.Kind);
        Assert.Equal("notes.txt", model.FileName);
        Assert.True(model.IsText);
        Assert.False(model.IsBusy);
        Assert.Equal("hello", model.Rows[0].Text);
        Assert.Equal("world", model.Rows[1].Text);
        Assert.Contains("UTF-8", model.StatusText, StringComparison.Ordinal);
        Assert.Equal([ViewerMode.Text, ViewerMode.Hex], model.Modes.Select(choice => choice.Mode));
        var list = Assert.IsType<ListBox>(Assert.IsType<ViewerView>(tool.Content).FindControl<ListBox>("RowList"));
        Assert.Equal(2, list.ItemCount);

        await model.SetModeAsync(ViewerMode.Hex);
        Assert.True(model.IsHex);
        Assert.StartsWith("00000000", model.Rows[0].Text, StringComparison.Ordinal);
        Assert.Contains("68 65 6C 6C 6F", model.Rows[0].Text, StringComparison.Ordinal);
        Assert.Contains("|hello", model.Rows[0].Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task F3OnAnotherFileReusesTheWindow()
    {
        using var rig = await Rig.Open(("a.txt", "first\n"), ("b.txt", "second\n"));
        rig.Select("a.txt");
        var first = await rig.ViewAsync();
        rig.Select("b.txt");
        var second = await rig.ViewAsync();

        Assert.Same(first, second);
        var model = Assert.IsType<ViewerViewModel>(second.View);
        Assert.Equal("b.txt", model.FileName);
        Assert.Equal("second", model.Rows[0].Text);
    }

    [AvaloniaFact]
    public async Task AFolderFirstIsSkippedAndFoldersOnlyShowNoSelection()
    {
        using var rig = await Rig.Open(("notes.txt", "hi\n"));
        Directory.CreateDirectory(Path.Combine(rig.Root, "sub"));
        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        rig.Select("sub", "notes.txt");

        var tool = await rig.ViewAsync();
        Assert.Equal("notes.txt", Assert.IsType<ViewerViewModel>(tool.View).FileName);
        tool.Close();

        rig.Select("sub");
        var pending = rig.Commands.ViewAsync();
        var shown = await rig.Dialogs.WaitShownAsync();
        Assert.Equal(rig.Text.Get("dlg.no_selection"), shown.Text);
        Assert.False(rig.Tools.IsOpen(ToolKind.Lister));
        rig.Dialogs.Answer(DialogResult.Ok(""));
        await pending;
    }

    [AvaloniaFact]
    public async Task ADbfOpensAsATableAndTheFilterNarrowsIt()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "names.dbf");
        File.WriteAllBytes(path, DbfSample.NameFile());
        var model = new ViewerViewModel(English());

        await model.OpenAsync(path);

        Assert.True(model.IsTable);
        Assert.False(model.CanFind);
        Assert.Contains("NAME", model.Header, StringComparison.Ordinal);
        Assert.Equal(2, model.Rows.Count);
        model.Filter = "bob";
        Assert.Single(model.Rows);
        Assert.Contains("Bob", model.Rows[0].Text, StringComparison.Ordinal);
        model.Filter = "";
        Assert.Equal(2, model.Rows.Count);
    }

    [AvaloniaFact]
    public async Task FindMovesTheCursorAndReportsNotFound()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(path, "Alpha\nbeta\nALPHA beta\n");
        var model = new ViewerViewModel(English());
        await model.OpenAsync(path);

        model.FindText = "alpha";
        Assert.True(model.Find(true));
        Assert.Equal(0, model.TextCursor);
        Assert.Equal(0, model.SelectedRow);
        Assert.True(model.Find(true));
        Assert.Equal(2, model.TextCursor);
        Assert.True(model.Find(false));
        Assert.Equal(0, model.TextCursor);

        model.MatchCase = true;
        model.FindText = "zzz";
        Assert.False(model.Find(true));
        Assert.Equal("Not found", model.Notice);
        model.FindText = "beta";
        Assert.True(model.Find(true));
        Assert.Equal("", model.Notice);
    }

    [AvaloniaFact]
    public async Task ASparseFileAboveMaxReadOpensAndScrollsToItsLastLoadedRow()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "big.bin");
        using (var stream = new FileStream(path, FileMode.Create))
        {
            stream.SetLength(ViewerLimits.MaxRead + (64L * 1024 * 1024));
        }

        var model = new ViewerViewModel(English());
        await model.OpenAsync(path);

        Assert.True(model.IsHex);
        Assert.Equal(ViewerLimits.MaxRead / ViewerLimits.HexWidth, model.Rows.Count);
        Assert.Equal(model.Rows.Count, model.Rows.Count());
        Assert.Equal(English().Get("lister.truncated"), model.TruncatedText);
        model.GoToEnd();
        Assert.Equal(model.Rows.Count - 1, model.SelectedRow);
        Assert.StartsWith("00FFFFF0", model.Rows[model.SelectedRow].Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AMissingFileShowsItsTypedError()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "gone.txt");
        var model = new ViewerViewModel(French());

        await model.OpenAsync(path);

        Assert.False(model.IsBusy);
        Assert.True(model.HasError);
        Assert.Equal("Introuvable: " + path, model.ErrorMessage);
        Assert.Empty(model.Rows);
        Assert.Empty(model.Modes);
    }

    [AvaloniaFact]
    public async Task TheWindowOpensAtOnceWithTheNameAndABusyState()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "slow.txt");
        File.WriteAllText(path, "x\n");
        var gate = new ManualResetEventSlim();
        var model = new ViewerViewModel(English(), file =>
        {
            gate.Wait(TestContext.Current.CancellationToken);
            return FileViewer.Open(file);
        });

        var opening = model.OpenAsync(path);

        Assert.True(model.IsBusy);
        Assert.Equal("slow.txt", model.FileName);
        Assert.Equal("Opening slow.txt...", model.Notice);
        gate.Set();
        await opening;
        Assert.False(model.IsBusy);
        Assert.Equal("x", model.Rows[0].Text);
    }

    [Fact]
    public void StringsExistInBothLanguages()
    {
        var en = English();
        var fr = French();
        string[] keys =
        [
            "lister.mode.text", "lister.mode.hex", "lister.mode.table", "lister.mode.image", "lister.wrap",
            "lister.match_case", "lister.find", "lister.find_next", "lister.find_prev", "lister.filter",
            "lister.not_found", "lister.opening", "lister.truncated",
        ];
        foreach (var key in keys)
        {
            Assert.NotEqual(key, en.Get(key));
            Assert.NotEqual(key, fr.Get(key));
        }

        Assert.Equal("Wrap", new ViewerViewModel(en).WrapLabel);
        Assert.Equal("Retour à la ligne", new ViewerViewModel(fr).WrapLabel);
        Assert.Equal("Suivant", new ViewerViewModel(fr).NextLabel);
    }

    private static ILocalizer English() => Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    private static ILocalizer French() => Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);


    private sealed class Rig : IDisposable
    {
        private readonly ToolWindowApp _app;

        private Rig(string root, PanelWorkspace workspace, ToolWindowApp app, FakeDialogService dialogs)
        {
            Root = root;
            Workspace = workspace;
            _app = app;
            Dialogs = dialogs;
            Text = app.Localizer;
            Commands = new ViewerCommands(workspace, app.Tools, dialogs, Text);
            Commands.Bind(new ShellContributions());
        }

        public string Root { get; }

        public PanelWorkspace Workspace { get; }

        public FakeDialogService Dialogs { get; }

        public ILocalizer Text { get; }

        public ViewerCommands Commands { get; }

        public IToolWindowHost Tools => _app.Tools;

        public ToolKind Kind => ToolKind.Lister;

        public static async Task<Rig> Open(params (string Name, string Content)[] files)
        {
            var root = Directory.CreateTempSubdirectory("d3sk-viewer-").FullName;
            foreach (var (name, content) in files)
            {
                File.WriteAllText(Path.Combine(root, name), content);
            }

            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
            await workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            return new Rig(root, workspace, ToolWindowApp.Open(), new FakeDialogService());
        }

        public void Select(params string[] names)
        {
            Workspace.Activate(PanelSide.Left);
            var snap = Workspace.Read(PanelSide.Left);
            var cursor = 0;
            for (var i = 0; i < snap.Entries.Count; i++)
            {
                if (snap.Entries[i].Name == names[0])
                {
                    cursor = i;
                }
            }

            Workspace.ApplySelection(PanelSide.Left, cursor, names.Select(name => Path.Combine(Root, name)).ToList());
        }

        public async Task<ToolWindow> ViewAsync()
        {
            await Commands.ViewAsync();
            return _app.Tools.Open(ToolKind.Lister);
        }

        public void Dispose()
        {
            _app.Dispose();
            Workspace.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("d3sk-viewer-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
