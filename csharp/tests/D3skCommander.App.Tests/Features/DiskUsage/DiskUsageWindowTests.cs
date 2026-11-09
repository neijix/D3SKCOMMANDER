using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.DiskUsage;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.DiskUsage;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Scripted = D3skCommander.App.Tests.Features.FileOps.FileOpsCommandTests.ScriptedDialogs;

namespace D3skCommander.App.Tests.Features.DiskUsage;

public partial class DiskUsageWindowTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [AvaloniaFact]
    public async Task OpensOnTheFolderUnderTheCursorAndListsTheChildrenBiggestFirst()
    {
        using var tree = new UsageTree();
        var panels = new DiskPanels(tree.Root, tree.Entry("big"));

        using var host = Host(panels, new Scripted());
        host.Window.Dispatch(CommandId.DiskUsage);
        var model = Model(host);
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(tree.Root, "big"), model.RootPath);
        Assert.Equal(["f1.bin", "sub"], model.Rows.Select(row => row.Node.Name).ToArray());
        var tool = host.Provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.DiskUsage);
        var list = tool.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "RowList");
        Assert.Equal(2, list.ItemCount);
        Assert.Contains("Size: 2.0 KB", Text(tool, "TotalsText"), StringComparison.Ordinal);
        Assert.False(model.IsScanning);
    }

    [AvaloniaFact]
    public async Task TheCursorOnAFileOrParentOpensTheShownFolder()
    {
        using var tree = new UsageTree();
        var file = new Entry("small.txt", Path.Combine(tree.Root, "small.txt"), false, false, false, 10, null, false, false, "");

        using var host = Host(new DiskPanels(tree.Root, file), new Scripted());
        host.Window.Dispatch(CommandId.DiskUsage);
        var model = Model(host);
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(tree.Root, model.RootPath);
        Assert.Equal(["big", "mid", "small.txt"], model.Rows.Select(row => row.Node.Name).ToArray());
    }

    [AvaloniaFact]
    public async Task ADoubleClickOrEnterDescendsAndUpComesBack()
    {
        using var tree = new UsageTree();
        using var host = Host(new DiskPanels(tree.Root, null), new Scripted());
        host.Window.Dispatch(CommandId.DiskUsage);
        var model = Model(host);
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
        var tool = host.Provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.DiskUsage);
        var list = tool.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "RowList");
        var up = tool.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "UpButton");
        Assert.False(up.IsEnabled);

        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(Path.Combine(tree.Root, "big"), model.Current!.Path);
        Assert.Equal(["f1.bin", "sub"], model.Rows.Select(row => row.Node.Name).ToArray());
        Assert.True(up.IsEnabled);

        up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(tree.Root, model.Current!.Path);
        Assert.Equal("big", model.Selected!.Node.Name);
        Assert.False(up.IsEnabled);
        Assert.False(model.Up());

        model.Selected = model.Rows.Single(row => row.Node.Name == "mid");
        Assert.True(model.DescendSelected());
        Assert.Equal("mid", model.Current!.Name);
        model.Selected = null;
        Assert.False(model.DescendSelected());
    }

    [AvaloniaFact]
    public async Task ClickingARingSegmentDescendsAndTheCentreGoesUp()
    {
        using var tree = new UsageTree();
        using var host = Host(new DiskPanels(tree.Root, null), new Scripted());
        host.Window.Dispatch(CommandId.DiskUsage);
        var model = Model(host);
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
        var tool = host.Provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.DiskUsage);
        tool.UpdateLayout();
        var chart = tool.GetVisualDescendants().OfType<RingChart>().Single();
        Assert.True(chart.Bounds.Width > 0);
        Assert.NotEmpty(chart.Segments);

        var big = chart.Segments.First(segment => segment.Level == 1 && segment.Path.EndsWith("big", StringComparison.Ordinal));
        Assert.True(chart.ClickAt(chart.PointOf(big)));
        Assert.Equal(Path.Combine(tree.Root, "big"), model.Current!.Path);
        Assert.Equal(["f1.bin", "sub"], model.Rows.Select(row => row.Node.Name).ToArray());
        Assert.Equal(model.Segments, chart.Segments);

        var (center, _, _) = chart.Metrics();
        Assert.True(chart.ClickAt(center));
        Assert.Equal(tree.Root, model.Current!.Path);

        var file = chart.Segments.First(segment => segment.Node is { IsFile: true });
        Assert.True(chart.ClickAt(chart.PointOf(file)));
        Assert.Equal(tree.Root, model.Current!.Path);
        Assert.Contains("small.txt", model.Describe(file), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ShowInPanelRevealsTheSelectedItem()
    {
        using var tree = new UsageTree();
        var panels = new DiskPanels(tree.Root, null);
        using var host = Host(panels, new Scripted());
        host.Window.Dispatch(CommandId.DiskUsage);
        var model = Model(host);
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        model.Selected = model.Rows.Single(row => row.Node.Name == "mid");
        Assert.True(await model.ShowInPanelAsync());

        Assert.Equal([Path.Combine(tree.Root, "mid")], panels.Revealed);
    }

    [AvaloniaFact]
    public async Task ACancelledScanSaysSoAndKeepsNoTree()
    {
        using var tree = new UsageTree();
        var model = new DiskUsageViewModel(Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En), new DiskPanels(tree.Root, null));

        var scan = model.BeginAsync(tree.Root);
        model.Cancel();
        await scan.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.False(model.IsScanning);
        Assert.Equal(model.Root is null ? "Scan cancelled." : "", model.Status);
    }

    [AvaloniaFact]
    public async Task ARemoteOrMissingFolderShowsAMessageAndNoWindow()
    {
        var dialogs = new Scripted();
        using var host = Host(new DiskPanels("ftp://example.invalid/pub", null), dialogs);

        host.Window.Dispatch(CommandId.DiskUsage);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Single(dialogs.Messages);
        Assert.Contains("local", dialogs.Messages[0], StringComparison.Ordinal);
        Assert.False(host.Provider.GetRequiredService<IToolWindowHost>().IsOpen(ToolKind.DiskUsage));
    }

    [AvaloniaFact]
    public void TheWindowTextIsBilingual()
    {
        var en = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        Assert.Equal("Disk usage…", en.Get(CommandCatalog.LabelKey(CommandId.DiskUsage)));
        var fr = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);
        Assert.Equal("Utilisation du disque…", fr.Get(CommandCatalog.LabelKey(CommandId.DiskUsage)));
        Assert.Equal("Utilisation du disque", fr.Get("tool.diskusage.title"));
        Assert.Equal(CommandId.DiskUsage, CommandCatalog.Bindings.Single(b => b.Gesture == new Gesture(GestureModifiers.Shift | GestureModifiers.Alt, GestureKey.L)).Command);
    }

    private static ShellHost Host(IPanelWorkspace panels, IDialogService dialogs) =>
        ShellHost.Open(Language.En, services =>
        {
            services.AddSingleton(panels);
            services.AddSingleton(dialogs);
        });

    private static DiskUsageViewModel Model(ShellHost host) => host.Provider.GetRequiredService<DiskUsageViewModel>();

    private static string Text(ToolWindow tool, string name) =>
        tool.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == name).Text ?? "";

    /// <summary>root/big/f1.bin 1500, root/big/sub/f2.bin 500, root/mid/m.bin 300, root/small.txt 10.</summary>
    private sealed class UsageTree : IDisposable
    {
        public UsageTree()
        {
            Root = Directory.CreateTempSubdirectory("d3sk-du-ui-").FullName;
            Write("big/f1.bin", 1500);
            Write("big/sub/f2.bin", 500);
            Write("mid/m.bin", 300);
            Write("small.txt", 10);
        }

        public string Root { get; }

        public Entry Entry(string folder) =>
            new(folder, Path.Combine(Root, folder), true, false, false, 0, null, false, false, "");

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private void Write(string relative, int length)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[length]);
        }
    }

    private sealed class DiskPanels(string path, Entry? cursor) : IPanelWorkspace
    {
        public List<string> Revealed { get; } = [];

        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) =>
            PanelSnapshot.Empty with { Path = path, Entries = cursor is null ? [] : [cursor], Cursor = 0 };

        public PanelCapture? Capture() => null;

        public Task<bool> GoAsync(PanelSide side, string target, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> RevealAsync(string target, CancellationToken cancellationToken = default)
        {
            Revealed.Add(target);
            return Task.FromResult(true);
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
