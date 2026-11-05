using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
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
    public async Task AFindStartedInTheF3WindowIsListedByTheJobService()
    {
        using var rig = await Rig.Open(("a.txt", "alpha\nbeta\n"));
        rig.Select("a.txt");
        var tool = await rig.ViewAsync();
        var model = Assert.IsType<ViewerViewModel>(tool.View);

        model.FindText = "beta";
        Assert.True(await model.FindAsync(true));

        Assert.Equal("Searching a.txt", Assert.Single(rig.Jobs.Jobs).Title);
    }

    [AvaloniaFact]
    public async Task RowsUseAFixedPitchFontOnACompactLineStep()
    {
        using var rig = await Rig.Open(("notes.txt", "iiii\nWWWW\nmore\n"));
        rig.Select("notes.txt");
        var tool = await rig.ViewAsync();
        var view = Assert.IsType<ViewerView>(tool.Content);
        var list = view.FindControl<ListBox>("RowList")!;
        tool.UpdateLayout();

        var first = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
        var second = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
        var text = first.GetVisualDescendants().OfType<TextBlock>().Last();
        Assert.True(FixedPitch.Is(text.FontFamily), text.FontFamily.ToString());
        Assert.Equal(new SolidColorBrush(Colors.Transparent).Color, ((ISolidColorBrush)list.Background!).Color);
        var step = second.Bounds.Y - first.Bounds.Y;
        Assert.True(step > 0 && step <= 1.5 * text.Bounds.Height, step.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public async Task AWrappedRowFillsTheListWidthAndNoMore()
    {
        using var rig = await Rig.Open(("long.txt", new string('x', 3000) + "\n"));
        rig.Select("long.txt");
        var tool = await rig.ViewAsync();
        var model = Assert.IsType<ViewerViewModel>(tool.View);
        var list = Assert.IsType<ViewerView>(tool.Content).FindControl<ListBox>("RowList")!;
        Assert.True(model.Wrap);

        foreach (var width in new[] { 520.0, 910.0 })
        {
            tool.Width = width;
            tool.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            tool.UpdateLayout();

            var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var text = item.GetVisualDescendants().OfType<TextBlock>().Last();
            Assert.Equal(model.Rows[0].Text, text.Text);
            Assert.True(model.Rows.Count > 1);
            // The rendered advance of one character of the row font, read from the row itself.
            var charWidth = text.Bounds.Width / text.Text!.Length;
            var room = list.Scroll!.Viewport.Width - 8;
            var used = (model.Rows[0].Gutter.Length + text.Text.Length) * charWidth;
            var at = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(used <= room + 0.01, $"{used} > {room} at {at}");
            Assert.True(used + charWidth > room, $"one more character fits in {room} at {at}");
        }
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
        var model = new ViewerViewModel(English(), new JobService());

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

        var model = new ViewerViewModel(English(), new JobService());
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
        var model = new ViewerViewModel(English(), new JobService());
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
        var model = new ViewerViewModel(French(), new JobService());

        await model.OpenAsync(path);

        Assert.False(model.IsBusy);
        Assert.True(model.HasError);
        Assert.Equal("Introuvable: " + path, model.ErrorMessage);
        Assert.Empty(model.Rows);
        Assert.Empty(model.Modes);
    }

    [AvaloniaFact]
    public async Task AnImageOpensInImageModeWithItsPixelSize()
    {
        var model = new ViewerViewModel(English(), new JobService());

        await model.OpenAsync(Fixture("pixel.png"));

        Assert.True(model.IsImage);
        Assert.Equal(new Avalonia.PixelSize(3, 2), model.Image?.PixelSize);
        Assert.Equal([ViewerMode.Image, ViewerMode.Hex], model.Modes.Select(choice => choice.Mode));
        Assert.StartsWith("3 x 2", model.StatusText, StringComparison.Ordinal);
        Assert.Equal("", model.Notice);

        await model.SetModeAsync(ViewerMode.Hex);
        Assert.True(model.IsHex);
        Assert.StartsWith("00000000", model.Rows[0].Text, StringComparison.Ordinal);
        await model.SetModeAsync(ViewerMode.Image);
        Assert.True(model.IsImage);
        Assert.NotNull(model.Image);
    }

    [AvaloniaFact]
    public async Task ATiffOpensInImageModeAndABrokenOneInHexWithTheNotice()
    {
        var model = new ViewerViewModel(English(), new JobService());

        await model.OpenAsync(Fixture("pixel.tif"));

        Assert.True(model.IsImage);
        var image = Assert.IsAssignableFrom<Bitmap>(model.Image);
        Assert.Equal(new Avalonia.PixelSize(3, 2), image.PixelSize);
        Assert.Equal([ViewerMode.Image, ViewerMode.Hex], model.Modes.Select(choice => choice.Mode));
        Assert.StartsWith("3 x 2", model.StatusText, StringComparison.Ordinal);
        var first = new byte[4];
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(first, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            image.CopyPixels(new Avalonia.PixelRect(0, 0, 1, 1), pinned.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            pinned.Free();
        }

        // The top-left pixel is red, whichever byte order the bitmap keeps.
        var red = image.Format == Avalonia.Platform.PixelFormats.Bgra8888 ? first[2] : first[0];
        Assert.Equal(255, red);
        Assert.Equal(255, first[3]);

        await model.OpenAsync(Fixture("corrupt.tif"));
        Assert.True(model.IsHex);
        Assert.Null(model.Image);
        Assert.Equal(English().Get("lister.image_failed"), model.Notice);
    }

    [AvaloniaFact]
    public async Task AReplacedImageIsDisposed()
    {
        var model = new ViewerViewModel(English(), new JobService());
        await model.OpenAsync(Fixture("pixel.png"));
        var first = model.Image!;

        await model.OpenAsync(Fixture("pixel.png"));

        Assert.Throws<ObjectDisposedException>(() => first.PixelSize);
        var second = model.Image!;
        Assert.NotSame(first, second);
        model.ReleaseImage();
        Assert.Throws<ObjectDisposedException>(() => second.PixelSize);
        Assert.Null(model.Image);
    }

    [AvaloniaFact]
    public async Task TheImageOfAStaleOpenIsDisposed()
    {
        using var dir = new TempDir();
        var slow = Path.Combine(dir.Path, "slow.png");
        File.Copy(Fixture("pixel.png"), slow);
        var gate = new ManualResetEventSlim();
        Bitmap? stale = null;
        ViewerImage.Decoded = (path, bitmap) =>
        {
            if (path == slow)
            {
                stale = bitmap;
            }
        };
        try
        {
            var model = new ViewerViewModel(English(), new JobService(), file =>
            {
                if (file == slow)
                {
                    gate.Wait(TestContext.Current.CancellationToken);
                }

                return FileViewer.Open(file);
            });

            var first = model.OpenAsync(slow);
            await model.OpenAsync(Fixture("corrupt.png"));
            gate.Set();
            await first;

            Assert.NotNull(stale);
            Assert.Throws<ObjectDisposedException>(() => stale.PixelSize);
            Assert.Null(model.Image);
        }
        finally
        {
            ViewerImage.Decoded = null;
        }
    }

    [AvaloniaFact]
    public async Task ACorruptImageOpensInHexWithALocalizedNotice()
    {
        var model = new ViewerViewModel(French(), new JobService());

        await model.OpenAsync(Fixture("corrupt.png"));

        Assert.True(model.IsHex);
        Assert.False(model.HasError);
        Assert.Null(model.Image);
        Assert.Equal([ViewerMode.Hex], model.Modes.Select(choice => choice.Mode));
        Assert.Equal(French().Get("lister.image_failed"), model.Notice);
        Assert.StartsWith("00000000", model.Rows[0].Text, StringComparison.Ordinal);
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
            "lister.not_found", "lister.opening", "lister.find_job", "lister.image_failed",
        ];
        foreach (var key in keys)
        {
            Assert.NotEqual(key, en.Get(key));
            Assert.NotEqual(key, fr.Get(key));
        }

        Assert.Equal("Wrap", new ViewerViewModel(en, new JobService()).WrapLabel);
        Assert.Equal("Retour à la ligne", new ViewerViewModel(fr, new JobService()).WrapLabel);
        Assert.Equal("Suivant", new ViewerViewModel(fr, new JobService()).NextLabel);
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

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "images", name);

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
            Jobs = new JobService();
            Commands = new ViewerCommands(workspace, app.Tools, dialogs, Text, Jobs);
            Commands.Bind(new ShellContributions());
        }

        public string Root { get; }

        public PanelWorkspace Workspace { get; }

        public FakeDialogService Dialogs { get; }

        public ILocalizer Text { get; }

        public JobService Jobs { get; }

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

/// <summary>Whether a font family resolves, on the running platform, to a typeface whose glyphs share one advance.</summary>
internal static class FixedPitch
{
    public static bool Is(FontFamily family)
    {
        var typeface = new Typeface(family);
        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs));
        return Advance(glyphs, 'i') == Advance(glyphs, 'W');
    }

    private static int Advance(GlyphTypeface glyphs, char c)
    {
        var glyph = glyphs.CharacterToGlyphMap[c];
        Assert.NotEqual((ushort)0, glyph);
        Assert.True(glyphs.TryGetHorizontalGlyphAdvance(glyph, out var advance));
        return (int)Math.Round(advance * 1000.0);
    }
}
