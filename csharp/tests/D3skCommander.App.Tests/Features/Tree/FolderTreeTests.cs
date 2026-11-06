using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Tree;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Tree;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Features.Tree;

public class FolderTreeTests
{
    [AvaloniaFact]
    public void TheToggleShowsTheTreeBesideTheListAndSavesTheChoicePerSide()
    {
        using var host = ShellHost.Open(Language.En);
        var settings = host.Provider.GetRequiredService<AppSettings>();
        var shell = host.Provider.GetRequiredService<IShellContributions>();
        var panes = host.Window.GetVisualDescendants().OfType<PanelPaneView>().ToList();
        var left = panes[0].FindControl<ContentControl>("TreeHost")!;
        var right = panes[1].FindControl<ContentControl>("TreeHost")!;
        try
        {
            Assert.False(left.IsVisible);
            Assert.False(settings.Preferences.Current.ShowTreeLeft);
            Assert.Equal(220, settings.Preferences.Current.TreeWidthLeft);

            Assert.True(shell.Dispatch(CommandId.ToggleTreeLeft));
            Assert.True(shell.Dispatch(CommandId.ToggleTree));
            Assert.True(shell.Dispatch(CommandId.ToggleTreeRight));

            Assert.True(settings.Preferences.Current.ShowTreeRight);
            Assert.False(settings.Preferences.Current.ShowTreeLeft);
            Assert.True(((ShellContributions)shell).CheckedState(CommandId.ToggleTreeRight));
            Assert.False(((ShellContributions)shell).CheckedState(CommandId.ToggleTreeLeft));
            foreach (var pane in panes)
            {
                ((PanelPaneModel)pane.DataContext!).Tick();
            }

            Dispatcher.UIThread.RunJobs();
            Assert.False(left.IsVisible);
            Assert.True(right.IsVisible);
        }
        finally
        {
            ResetTrees(settings);
        }
    }

    [AvaloniaFact]
    public async Task NavigatingThePanelExpandsTheChainAndSelectsTheFolder()
    {
        using var rig = await Rig.Opened();

        await rig.Workspace.GoAsync(PanelSide.Left, rig.Deep, TestContext.Current.CancellationToken);
        await rig.WaitUntil(() => rig.Tree.Selected?.Path == rig.Deep);

        Assert.True(rig.Tree.Find(rig.Path("a"))!.IsExpanded);
        Assert.False(rig.Tree.Find(rig.Path("a", "b"))!.IsExpanded);
        Assert.Equal(["a", "z"], rig.Tree.Find(rig.Root)!.Children.Select(c => c.Name));
        Assert.Equal(PanelSide.Left, rig.Pane.Side);
    }

    [AvaloniaFact]
    public async Task APathUnderNoRootClearsTheSelection()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var parent = FolderTree.RootFor(Path.GetTempPath(), [home]) is null ? Path.GetTempPath() : AppContext.BaseDirectory;
        Assert.SkipWhen(FolderTree.RootFor(parent, [home]) is not null, "every writable folder is under the home folder, a tree root");
        using var rig = await Rig.Opened();
        await rig.WaitUntil(() => rig.Tree.Selected?.Path == rig.Root);
        var elsewhere = Directory.CreateDirectory(Path.Combine(parent, "d3sk-tree-away-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await rig.Workspace.GoAsync(PanelSide.Left, elsewhere, TestContext.Current.CancellationToken);
            await rig.WaitUntil(() => rig.Tree.Selected is null);
        }
        finally
        {
            Directory.Delete(elsewhere, true);
        }
    }

    [AvaloniaFact]
    public async Task ArrowKeysOnlyMoveTheSelectionAndEnterGoesThere()
    {
        using var rig = await Rig.Opened();
        await rig.WaitUntil(() => rig.Tree.Selected?.Path == rig.Root);
        var root = rig.Tree.Find(rig.Root)!;
        root.IsExpanded = true;
        await rig.WaitUntil(() => root.Loaded);
        var tree = rig.View.Tree!.View;
        await rig.WaitUntil(() => tree.TreeContainerFromItem(root) is not null);
        Assert.True(((Control)tree.TreeContainerFromItem(root)!).Focus());
        Assert.Equal(root, ((Control?)rig.Host.FocusManager!.GetFocusedElement())?.DataContext);

        rig.Press(PhysicalKey.ArrowDown);
        rig.Press(PhysicalKey.ArrowDown);
        await rig.Settle();

        Assert.Equal(rig.Path("z"), rig.Tree.Selected?.Path);
        Assert.Equal(rig.Root, rig.Workspace.Read(PanelSide.Left).Path);

        rig.Press(PhysicalKey.Enter);
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Path == rig.Path("z"));
        await rig.Settle();

        Assert.Equal(rig.Path("z"), rig.Tree.Selected?.Path);
    }

    [AvaloniaFact]
    public async Task AClickOnANodeGoesThereAndTheHistoryGrowsByOneStep()
    {
        using var rig = await Rig.Opened();
        await rig.WaitUntil(() => rig.Tree.Selected?.Path == rig.Root);
        var root = rig.Tree.Find(rig.Root)!;
        root.IsExpanded = true;
        await rig.WaitUntil(() => root.Loaded);
        var target = rig.Tree.Find(rig.Path("z"))!;
        var tree = rig.View.Tree!.View;
        await rig.WaitUntil(() => tree.TreeContainerFromItem(target) is not null);
        var container = (Control)tree.TreeContainerFromItem(target)!;
        var point = container.TranslatePoint(new Point(40, 10), rig.Host)!.Value;

        rig.Host.MouseDown(point, MouseButton.Left);
        rig.Host.MouseUp(point, MouseButton.Left);
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Path == rig.Path("z"));
        await rig.Settle();

        Assert.Same(target, rig.Tree.Selected);
        await rig.Workspace.BackAsync(TestContext.Current.CancellationToken);
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Path == rig.Root);
        await rig.Workspace.BackAsync(TestContext.Current.CancellationToken);
        await rig.Settle();
        Assert.Equal(rig.Root, rig.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task ExpandingANodeLoadsItsChildrenAndAFolderThatCannotBeReadShowsAPlaceholder()
    {
        using var rig = await Rig.Opened();
        var root = rig.Tree.Find(rig.Root)!;
        Assert.True(root.Children[0].IsPlaceholder);

        root.IsExpanded = true;
        await rig.WaitUntil(() => root.Loaded);

        Assert.Equal(["a", "z"], root.Children.Select(c => c.Name));
        var missing = rig.Tree.NewNode("gone", rig.Path("gone"));
        await rig.Tree.LoadAsync(missing);
        Assert.Equal(FolderNodeKind.NoAccess, Assert.Single(missing.Children).Kind);
    }

    [AvaloniaFact]
    public async Task RefreshingThePanelReadsTheNodeAgain()
    {
        using var rig = await Rig.Opened();
        await rig.Workspace.GoAsync(PanelSide.Left, rig.Path("a"), TestContext.Current.CancellationToken);
        await rig.WaitUntil(() => rig.Tree.Selected?.Path == rig.Path("a"));
        var folder = rig.Tree.Find(rig.Path("a"))!;
        await rig.Tree.LoadAsync(folder);
        Assert.Equal(["b"], folder.Children.Select(c => c.Name));

        Directory.CreateDirectory(rig.Path("a", "new"));
        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);

        await rig.WaitUntil(() => folder.Children.Count == 2);
        Assert.Equal(["b", "new"], folder.Children.Select(c => c.Name));
    }

    private static void ResetTrees(AppSettings settings)
    {
        var preferences = settings.Preferences.Current;
        preferences.ShowTreeLeft = false;
        preferences.ShowTreeRight = false;
        settings.Preferences.Save(preferences);
    }

    private sealed class FakeDrives(params DriveEntry[] list) : IDriveSource
    {
        public IReadOnlyList<DriveEntry> Mounted() => list;
    }

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<string?> PickFolderAsync(string title, string? start, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class Rig : IDisposable
    {
        private Rig(string root)
        {
            Root = root;
            var text = TextCatalog.Load(Language.En, Language.En);
            Workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker());
            Pane = new PanelPaneModel(PanelSide.Left, Workspace, text, new FakeDialogService(), null, new FakeDrives(new DriveEntry(root, "Temp", "ext4", 10, 100)));
            View = (PanelPaneView)Pane.CreateContent();
            Host = new Window { Width = 900, Height = 500, Content = View };
        }

        public string Root { get; }

        public string Deep => Path("a", "b");

        public PanelWorkspace Workspace { get; }

        public PanelPaneModel Pane { get; }

        public FolderTreeModel Tree => Pane.Tree;

        public PanelPaneView View { get; }

        public Window Host { get; }

        public void Press(PhysicalKey key) => Host.KeyPressQwerty(key, RawInputModifiers.None);

        public static async Task<Rig> Opened()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-tree-app-").FullName;
            Directory.CreateDirectory(System.IO.Path.Combine(root, "a", "b"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "z"));
            var rig = new Rig(root);
            await rig.Workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            rig.Workspace.ToggleTree(PanelSide.Left);
            rig.Host.Show();
            await rig.WaitUntil(() => rig.Tree.Roots.Any(r => r.Path == root));
            return rig;
        }

        public string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

        public async Task WaitUntil(Func<bool> done)
        {
            for (var i = 0; i < 250 && !done(); i++)
            {
                await Settle(1);
            }

            Assert.True(done());
        }

        public async Task Settle(int beats = 15)
        {
            for (var i = 0; i < beats; i++)
            {
                Pane.Tick();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        public void Dispose()
        {
            Host.Close();
            Workspace.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
