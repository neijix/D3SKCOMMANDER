using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Catalog;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Catalog;

public class CatalogDialogTests
{
    [AvaloniaFact]
    public async Task CreateSearchesOfflineAndOpensTheHit()
    {
        var stop = TestContext.Current.CancellationToken;
        using var root = new TempDir();
        var source = Path.Combine(root.Path, "photos");
        var other = Path.Combine(root.Path, "out");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "docs"));
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(source, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(source, "docs", "b.md"), "12");
        var expected = Path.GetFullPath(Path.Combine(source, "docs", "b.md"));
        var output = Path.Combine(other, "mine.d3cat");
        var text = English();
        var dialogs = new FakeDialogService();
        using var panels = await OpenPanels(source, other, stop);
        var dialog = new CatalogDialog(new JobService(), dialogs, text);
        var commands = new CatalogCommands(panels, dialog, dialogs, text, new NoOwner());

        var running = commands.RunAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Text, shown.Kind);
        Assert.Equal(text.Get("dlg.out_file"), shown.Text);
        Assert.EndsWith("photos" + DiskCatalog.FileExtension, shown.InitialText);
        dialogs.Answer(DialogResult.Ok(output));
        await running.WaitAsync(TimeSpan.FromSeconds(15), stop);
        Assert.True(dialog.IsOpen);

        Directory.Delete(source, true);
        dialog.Query = "B.MD";
        Assert.True(await dialog.SearchAsync());
        Assert.Equal("b.md", dialog.Hits.CellText(0, 0));
        Assert.Equal("docs/b.md", dialog.Hits.CellText(0, 1));
        Assert.Equal("2 B", dialog.Hits.CellText(0, 2));
        var window = new CatalogWindow(dialog, text);
        window.Show();
        window.UpdateLayout();
        Assert.True(window.Hits.IsRealized(0));
        Assert.Equal(text.Get("name"), window.Hits.HeaderText("name"));
        Assert.Equal(["b.md", "docs/b.md", "2 B"], window.Hits.RenderedCells(0).Take(3));
        Assert.True(dialog.OpenSelected());
        Assert.Equal(expected, dialog.OpenedPath);
        Assert.False(File.Exists(expected));
        Assert.Equal(text.Get("catalog.opened"), dialog.Status);
        Assert.Equal(output, dialog.OutputPath);
        Assert.True(DiskCatalog.IsCatalog(output));
        window.Close();
        Assert.False(dialog.IsOpen);
    }

    [AvaloniaFact]
    public async Task TheCatalogOpensAsAnOwnedModalWindowAndSearchesInIt()
    {
        var stop = TestContext.Current.CancellationToken;
        using var root = new TempDir();
        var source = Path.Combine(root.Path, "photos");
        var other = Path.Combine(root.Path, "out");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(source, "needle.txt"), "hello");
        File.WriteAllText(Path.Combine(source, "other.txt"), "hello");
        var output = Path.Combine(other, "mine.d3cat");
        var text = English();
        var dialogs = new FakeDialogService();
        using var host = ShellHost.Open(Language.En);
        using var panels = await OpenPanels(source, other, stop);
        var dialog = new CatalogDialog(new JobService(), dialogs, text);
        var commands = new CatalogCommands(panels, dialog, dialogs, text, new FixedOwner(host.Window));
        CatalogWindow? window = null;
        try
        {
            var running = commands.RunAsync();
            await dialogs.WaitShownAsync();
            dialogs.Answer(DialogResult.Ok(output));
            await running.WaitAsync(TimeSpan.FromSeconds(15), stop);
            window = await WaitForWindow(host.Window, stop);

            Assert.True(window.IsVisible);
            Assert.Same(host.Window, window.Owner);
            Assert.Contains(window, ((Window)host.Window).OwnedWindows);
            Assert.True(window.IsDialog);

            dialog.Query = string.Empty;
            Assert.True(await dialog.SearchAsync());
            window.UpdateLayout();
            Assert.Equal(["needle.txt", "other.txt"], Names(window));

            dialog.Query = "needle";
            Assert.True(await dialog.SearchAsync());
            window.UpdateLayout();
            Assert.Equal(["needle.txt"], Names(window));
        }
        finally
        {
            window?.Close();
        }

        Assert.False(dialog.IsOpen);
    }

    private static string[] Names(CatalogWindow window)
    {
        var names = new List<string>();
        for (var row = 0; window.Hits.IsRealized(row); row++)
        {
            names.Add(window.Hits.RenderedCells(row).First());
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }

    private static async Task<CatalogWindow> WaitForWindow(MainWindow main, CancellationToken stop)
    {
        for (var i = 0; i < 200; i++)
        {
            if (((Window)main).OwnedWindows.OfType<CatalogWindow>().FirstOrDefault() is { } found)
            {
                return found;
            }

            await Task.Delay(25, stop);
        }

        throw new TimeoutException("The catalog window never opened.");
    }

    [AvaloniaFact]
    public async Task PlainTextNamedD3catIsNotSearched()
    {
        var text = English();
        var dialogs = new FakeDialogService();
        var dialog = new CatalogDialog(new JobService(), dialogs, text);
        using var root = new TempDir();
        var path = Path.Combine(root.Path, "notes.d3cat");
        File.WriteAllText(path, "just a note\nwithout a header\n");
        dialog.Prepare(root.Path, path);

        var pending = dialog.SearchAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, shown.Kind);
        Assert.Contains(text.Get("catalog.not_catalog"), shown.Text, StringComparison.Ordinal);
        dialogs.Answer(DialogResult.Ok());
        Assert.False(await pending);
        Assert.False(dialog.IsOpen);
        Assert.Null(dialog.Hits.CellText(0, 0));
    }

    [AvaloniaFact]
    public async Task CancelLeavesNoCatalog()
    {
        var stop = TestContext.Current.CancellationToken;
        using var root = new TempDir();
        var source = Path.Combine(root.Path, "photos");
        var other = Path.Combine(root.Path, "out");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(other);
        var dialogs = new FakeDialogService();
        using var panels = await OpenPanels(source, other, stop);
        var dialog = new CatalogDialog(new JobService(), dialogs, English());
        var commands = new CatalogCommands(panels, dialog, dialogs, English(), new NoOwner());

        var running = commands.RunAsync();
        await dialogs.WaitShownAsync();
        dialogs.Answer(DialogResult.Cancel());
        await running.WaitAsync(TimeSpan.FromSeconds(15), stop);

        Assert.False(dialog.IsOpen);
        Assert.Empty(Directory.GetFiles(other, "*" + DiskCatalog.FileExtension));
    }

    [AvaloniaFact]
    public void RegisterBindsTheCatalogCommand()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalizer>(English());
        services.AddSingleton<IDialogService>(new FakeDialogService());
        services.AddSingleton<IJobService, JobService>();
        services.AddSingleton<IPanelWorkspace>(new IdlePanels());
        services.AddSingleton<IDialogOwner>(new NoOwner());
        CatalogModule.Register(services);
        using var provider = services.BuildServiceProvider();
        var module = provider.GetRequiredService<CatalogModule>();
        var shell = new ShellContributions();
        module.Contribute(shell);

        Assert.True(shell.CanExecute(CommandId.Catalog));
        Assert.True(shell.Dispatch(CommandId.Catalog));
        var dialog = provider.GetRequiredService<CatalogDialog>();
        Assert.Empty(shell.UnderPanels);
        Assert.False(dialog.IsOpen);
    }

    [AvaloniaFact]
    public void CatalogStringsAreBilingual()
    {
        var text = English();
        AssertPair(text, "catalog.root", "Folder to catalog", "Dossier à cataloguer");
        AssertPair(text, "catalog.create", "Create catalog", "Créer le catalogue");
        AssertPair(text, "catalog.find", "Find", "Rechercher");
        AssertPair(text, "catalog.search", "Search", "Chercher");
        AssertPair(text, "catalog.open", "Open", "Ouvrir");
        AssertPair(text, "catalog.shown", "{0} files", "{0} fichiers");
        AssertPair(text, "catalog.opened", "Opened", "Ouvert");
        AssertPair(text, "catalog.not_catalog", "This file is not a disk catalog.", "Ce fichier n'est pas un catalogue de disque.");
    }

    private static void AssertPair(Localizer text, string key, string english, string french)
    {
        Assert.Equal(english, text.Get(key, Language.En));
        Assert.Equal(french, text.Get(key, Language.Fr));
        Assert.NotEqual(text.Get(key, Language.En), text.Get(key, Language.Fr));
    }

    private static Localizer English() =>
        Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    private static async Task<PanelWorkspace> OpenPanels(string left, string right, CancellationToken cancellationToken)
    {
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(left, right, cancellationToken);
        return workspace;
    }

    private sealed class NoOwner : IDialogOwner
    {
        public MainWindow? Current => null;
    }

    private sealed class FixedOwner(MainWindow window) : IDialogOwner
    {
        public MainWindow? Current => window;
    }

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class IdlePanels : IPanelWorkspace
    {
        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) => PanelSnapshot.Empty;

        public PanelCapture? Capture() => null;

        public Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("d3sk-catalog-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
