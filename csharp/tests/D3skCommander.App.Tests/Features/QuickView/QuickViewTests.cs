using System.IO.Compression;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickView;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Smoke;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.QuickView;

public class QuickViewTests
{
    [AvaloniaFact]
    public async Task CtrlQShowsTheFileInTheOtherPanelAndCtrlQAgainRestoresIt()
    {
        using var rig = await Rig.Open();
        var pane = rig.RightPane;
        Assert.False(pane.HasOverlay);

        rig.Toggle();
        await rig.Settle();

        Assert.True(rig.Commands.IsOn);
        Assert.Equal(PanelSide.Right, rig.Commands.Target);
        Assert.True(pane.HasOverlay);
        Assert.Equal("alpha\n", rig.Commands.Panel!.ShownText);
        Assert.False(rig.LeftPane.HasOverlay);

        rig.Toggle();

        Assert.False(rig.Commands.IsOn);
        Assert.False(pane.HasOverlay);
    }

    [AvaloniaFact]
    public async Task ThePreviewFollowsTheCursorAfterThePause()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        await rig.Settle();
        Assert.Equal("alpha", rig.ShownFirstRow());

        rig.MoveCursorTo("b.txt");
        rig.Commands.Tick();
        Assert.Equal("alpha", rig.ShownFirstRow());

        rig.Commands.Tick();
        rig.Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        rig.Commands.Tick();
        await rig.WaitFor(() => rig.ShownFirstRow() == "beta");

        Assert.Equal("beta", rig.ShownFirstRow());
    }

    [AvaloniaFact]
    public async Task AnImageShowsAsAnImageAndAFolderAsASummary()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        rig.MoveCursorTo("pixel.png");
        rig.Commands.Tick();
        rig.Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        rig.Commands.Tick();
        await rig.WaitFor(() => rig.Commands.Panel!.ShownPicture is not null);
        Assert.NotNull(rig.Commands.Panel!.ShownPicture);

        rig.MoveCursorTo("sub");
        rig.Commands.Tick();
        rig.Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        rig.Commands.Tick();
        await rig.WaitFor(() => rig.Commands.Panel!.SummaryText.Contains("items", StringComparison.Ordinal));

        Assert.False(rig.Commands.Panel!.ShowsViewer);
        Assert.Contains("sub", rig.Commands.Panel.SummaryText, StringComparison.Ordinal);
        Assert.Contains("2 items", rig.Commands.Panel.SummaryText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheOtherPanelGettingTheFocusRestoresTheList()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        await rig.Settle(); // the load reads a.txt in the background; Windows cannot delete it mid-read

        rig.Workspace.Activate(PanelSide.Right);
        rig.Commands.Tick();

        Assert.False(rig.Commands.IsOn);
        Assert.False(rig.RightPane.HasOverlay);
    }

    [AvaloniaFact]
    public async Task ThePreviewModeStaysOnItsPanelAndFollowsTheOtherOne()
    {
        using var rig = await Rig.Open();
        rig.Workspace.Activate(PanelSide.Right);

        rig.Commands.TogglePreview();
        await rig.Settle();

        Assert.True(rig.Commands.IsSticky);
        Assert.Equal(PanelSide.Right, rig.Commands.Target);
        Assert.Equal(PanelSide.Left, rig.Workspace.ActiveSide);
        Assert.True(rig.RightPane.HasOverlay);
        Assert.Equal("alpha", rig.ShownFirstRow());

        rig.Workspace.Activate(PanelSide.Right);
        rig.Commands.Tick();
        Assert.True(rig.Commands.IsOn);

        rig.RestOn("b.txt");
        await rig.WaitFor(() => rig.ShownFirstRow() == "beta");
        Assert.Equal("beta", rig.ShownFirstRow());

        rig.Commands.TogglePreview();
        Assert.False(rig.Commands.IsOn);
        Assert.False(rig.RightPane.HasOverlay);
    }

    [AvaloniaFact]
    public async Task CodeIsColouredAndABinaryFileGoesToTheLister()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        rig.RestOn("Program.cs");
        await rig.WaitFor(() => rig.Commands.Panel!.ShownText == "class A { }\n");

        Assert.Equal("C#", rig.Commands.Panel!.ShownHighlighting);

        rig.RestOn("tool.bin");
        await rig.WaitFor(() => rig.Commands.Panel!.ShowsViewer);
        Assert.True(rig.Commands.Panel!.ShowsViewer);
    }

    [AvaloniaFact]
    public async Task AnArchiveShowsItsListFoldersFirst()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        rig.RestOn("pack.zip");
        await rig.WaitFor(() => rig.Commands.Panel!.ArchiveRows.Count > 0);

        var rows = rig.Commands.Panel!.ArchiveRows;
        Assert.Equal("docs/", rows[0].Name);
        Assert.Contains(rows, row => row.Name == "main.c" && row.Size == "9 B");
        Assert.Contains(rows, row => row.Name == "docs/readme.txt" && row.Size == "5 B");
    }

    [AvaloniaFact]
    public async Task APdfShowsItsPagesAndARawPhotoItsPreview()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        rig.RestOn("page.pdf");
        await rig.WaitFor(() => rig.Commands.Panel!.PageCount == 1);
        Assert.Equal(1, rig.Commands.Panel!.PageCount);

        rig.RestOn("shot.cr2");
        await rig.WaitFor(() => rig.Commands.Panel!.ShownPicture is not null);
        Assert.NotNull(rig.Commands.Panel!.ShownPicture);
        Assert.Equal(0, rig.Commands.Panel.PageCount);
    }

    [Fact]
    public void FolderTextExistsInBothLanguages()
    {
        foreach (var language in new[] { Language.En, Language.Fr })
        {
            var text = Localizer.Load(settingsLanguage: language, systemLanguage: language);
            foreach (var key in new[] { "quickview.items", "quickview.unreadable", "quickview.archive_count", "quickview.archive_error", "quickview.pdf_error" })
            {
                Assert.NotEqual(key, text.Get(key));
            }
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _root;

        private Rig(string root, PanelWorkspace workspace)
        {
            _root = root;
            Workspace = workspace;
            Text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            Clock = new FakeTimeProvider();
            Commands = new QuickViewCommands(workspace, Text, new JobService(), Clock);
            var dialogs = new FakeDialogService();
            LeftPane = new PanelPaneModel(PanelSide.Left, workspace, Text, dialogs);
            RightPane = new PanelPaneModel(PanelSide.Right, workspace, Text, dialogs);
        }

        public PanelWorkspace Workspace { get; }

        public ILocalizer Text { get; }

        public FakeTimeProvider Clock { get; }

        public QuickViewCommands Commands { get; }

        public PanelPaneModel LeftPane { get; }

        public PanelPaneModel RightPane { get; }

        public ViewerViewModel Viewer =>
            (ViewerViewModel)((Avalonia.Controls.Control)Commands.Panel!.Content!).DataContext!;

        public static async Task<Rig> Open()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-quick-").FullName;
            File.WriteAllText(Path.Combine(root, "a.txt"), "alpha\n");
            File.WriteAllText(Path.Combine(root, "b.txt"), "beta\n");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "images", "pixel.png"), Path.Combine(root, "pixel.png"));
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "x"), "x");
            File.WriteAllText(Path.Combine(root, "sub", "y"), "y");
            AddPreviewSamples(root);
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
            await workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            workspace.Activate(PanelSide.Left);
            var rig = new Rig(root, workspace);
            rig.MoveCursorTo("a.txt");
            return rig;
        }

        public void Toggle() => Commands.Toggle();

        public void MoveCursorTo(string name)
        {
            var snap = Workspace.Read(PanelSide.Left);
            for (var i = 0; i < snap.Entries.Count; i++)
            {
                if (snap.Entries[i].Name == name)
                {
                    Workspace.ApplySelection(PanelSide.Left, i, []);
                    return;
                }
            }

            throw new InvalidOperationException(name);
        }

        public string ShownFirstRow() => (Commands.Panel?.ShownText ?? "").Split('\n')[0];

        /// <summary>The cursor rests on <paramref name="name"/> past the pause, so the view loads it.</summary>
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

        public Task Settle() => WaitFor(() => Commands.Panel?.ShownText is not null);

        public void Dispose()
        {
            Commands.Stop();
            Workspace.Dispose();
            Directory.Delete(_root, true);
        }
    }

    // One file of each kind the preview shows its own way, plus a binary one for the lister.
    private static void AddPreviewSamples(string root)
    {
        File.WriteAllText(Path.Combine(root, "Program.cs"), "class A { }\n");
        File.WriteAllBytes(Path.Combine(root, "tool.bin"), [0x4D, 0x5A, 0, 0, 1, 2, 3]);
        using (var zip = ZipFile.Open(Path.Combine(root, "pack.zip"), ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("docs/readme.txt").Open()))
            {
                writer.Write("hello");
            }

            using (var writer = new StreamWriter(zip.CreateEntry("main.c").Open()))
            {
                writer.Write("int main;");
            }
        }

        // PDFium rebuilds the missing cross-reference table, so three objects make a page.
        File.WriteAllText(
            Path.Combine(root, "page.pdf"),
            "%PDF-1.4\n1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n2 0 obj <</Type/Pages/Kids[3 0 R]/Count 1>> endobj\n"
            + "3 0 obj <</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]>> endobj\ntrailer <</Root 1 0 R>>\n%%EOF\n");

        // A RAW file is a TIFF header (no orientation tag here) with the camera's JPEG preview inside.
        byte[] tiff = [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "images", "turned6.jpg"));
        File.WriteAllBytes(Path.Combine(root, "shot.cr2"), [.. tiff, .. jpeg]);
    }

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
