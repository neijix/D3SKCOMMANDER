using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.ToolWindows;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Jobs;
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
        var jobs = new JobService();
        var model = new ViewerViewModel(English(), jobs);
        await model.OpenAsync(path);

        model.FindText = "alpha";
        Assert.True(await model.FindAsync(true));
        Assert.Equal(0, model.TextCursor);
        Assert.Equal(0, model.SelectedRow);
        Assert.True(await model.FindAsync(true));
        Assert.Equal(2, model.TextCursor);
        Assert.True(await model.FindAsync(false));
        Assert.Equal(0, model.TextCursor);
        Assert.Equal("Searching a.txt", jobs.Jobs[0].Title);

        model.MatchCase = true;
        model.FindText = "zzz";
        Assert.False(await model.FindAsync(true));
        Assert.Equal("Not found", model.Notice);
        model.FindText = "beta";
        Assert.True(await model.FindAsync(true));
        Assert.Equal("", model.Notice);
    }

    [AvaloniaFact]
    public async Task ASparseFileAboveMaxReadShowsAPageAndScrollsToItsLastByte()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "big.bin");
        var size = ViewerLimits.MaxRead + (64L * 1024 * 1024);
        MakeSparse(path, size);

        var model = new ViewerViewModel(English());
        await model.OpenAsync(path);

        Assert.True(model.IsHex);
        Assert.Equal(ViewerLimits.PageSize / ViewerLimits.HexWidth, model.Rows.Count);
        Assert.Equal(size / ViewerLimits.HexWidth - 1, model.ScrollMaximum);
        var view = Assert.IsType<ViewerView>(model.CreateContent());
        var bar = view.FindControl<ScrollBar>("PageScroll")!;
        Assert.True(bar.IsVisible);
        Assert.Equal(model.ScrollMaximum, bar.Maximum);

        await model.GoToEndAsync();
        Assert.False(model.IsBusy);
        Assert.Equal(model.Rows.Count - 1, model.SelectedRow);
        Assert.StartsWith((size - 16).ToString("X8", System.Globalization.CultureInfo.InvariantCulture), model.Rows[model.SelectedRow].Text, StringComparison.Ordinal);
        await model.NextPageAsync();
        Assert.StartsWith((size - 16).ToString("X8", System.Globalization.CultureInfo.InvariantCulture), model.Rows[^1].Text, StringComparison.Ordinal);

        await model.JumpAsync(0);
        Assert.StartsWith("00000000", model.Rows[0].Text, StringComparison.Ordinal);
        await model.NextPageAsync();
        Assert.StartsWith(ViewerLimits.PageSize.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), model.Rows[0].Text, StringComparison.Ordinal);
        await model.PreviousPageAsync();
        Assert.StartsWith("00000000", model.Rows[0].Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task FindReachesTextPlacedPastMaxReadAndSelectsItsRow()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "big.bin");
        var at = ViewerLimits.MaxRead + 4096L;
        MakeSparse(path, ViewerLimits.MaxRead + (64L * 1024 * 1024), (at, "needle"));
        var jobs = new JobService();
        var model = new ViewerViewModel(English(), jobs);
        await model.OpenAsync(path);

        model.FindText = "NEEDLE";
        Assert.True(await model.FindAsync(true));

        Assert.Equal(at, model.HexCursor);
        Assert.True(model.SelectedRow >= 0);
        Assert.StartsWith(at.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), model.Rows[model.SelectedRow].Text, StringComparison.Ordinal);
        Assert.Contains("|needle", model.Rows[model.SelectedRow].Text, StringComparison.Ordinal);
        Assert.Single(jobs.Jobs);

        Assert.True(await model.FindAsync(true));
        Assert.Equal(at, model.HexCursor);
        model.FindText = "absent";
        Assert.False(await model.FindAsync(false));
        Assert.Equal("Not found", model.Notice);
    }

    [AvaloniaFact]
    public async Task TextPagesFollowEachOtherAndFindCrossesPages()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "lines.txt");
        var builder = new StringBuilder();
        for (var i = 0; builder.Length < 2 * ViewerLimits.PageSize + 1000; i++)
        {
            builder.Append("line ").Append(i).Append(i == 120000 ? " marker" : "").Append('\n');
        }

        File.WriteAllText(path, builder.ToString());
        var model = new ViewerViewModel(English());
        model.SetColumns(10000);
        await model.OpenAsync(path);

        Assert.True(model.IsText);
        Assert.Equal("line 0", model.Rows[0].Text);
        var count = model.Rows.Count;
        await model.NextPageAsync();
        Assert.Equal("line " + count, model.Rows[0].Text);
        await model.PreviousPageAsync();
        Assert.Equal("line 0", model.Rows[0].Text);

        model.FindText = "marker";
        Assert.True(await model.FindAsync(true));
        Assert.Contains("line 120000 marker", model.Rows[model.SelectedRow].Text, StringComparison.Ordinal);
        Assert.Equal("marker", model.FindText);
        Assert.True(model.ScrollValue > 0);
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
        var model = new ViewerViewModel(English(), new JobService(), file =>
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
            "lister.not_found", "lister.opening", "lister.find_job",
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

    private static void MakeSparse(string path, long length, params (long Offset, string Text)[] texts)
    {
        using var stream = new FileStream(path, FileMode.Create);
        stream.SetLength(length);
        foreach (var (offset, text) in texts)
        {
            stream.Position = offset;
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
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
