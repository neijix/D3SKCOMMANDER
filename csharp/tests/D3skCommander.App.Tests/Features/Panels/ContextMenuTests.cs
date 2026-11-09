using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Features.Panels;

public class ContextMenuTests
{
    [Fact]
    public void ShiftF10IsTheKeyOfTheContextMenuCommand()
    {
        Assert.Equal(CommandId.ContextMenu, CommandCatalog.Match(new Gesture(GestureModifiers.Shift, GestureKey.F10)));
        Assert.Null(CommandCatalog.Match(new Gesture(GestureModifiers.None, GestureKey.F10)));
    }

    [AvaloniaFact]
    public async Task ARowRightClickMovesTheCursorAndOpensTheCoreEntriesWithLabelsRulesAndShortcuts()
    {
        using var rig = await Rig.Opened();

        rig.RightClick(rig.Index("b.txt"));

        Assert.Equal(rig.Index("b.txt"), rig.Workspace.Read(PanelSide.Left).Cursor);
        var menu = Assert.IsType<ContextMenu>(rig.View.FileMenu);
        var items = menu.Items.ToList();
        Assert.IsType<MenuItem>(items[0]);
        Assert.StartsWith(rig.Label(CommandId.Open), Header(items[0]), StringComparison.Ordinal);
        Assert.IsNotType<Separator>(items[^1]);
        Assert.Contains(items, item => item is Separator);
        Assert.DoesNotContain(items.Zip(items.Skip(1)), pair => pair.First is Separator && pair.Second is Separator);
        var copy = rig.Item(menu, CommandId.ClipCopy);
        Assert.Contains(CommandCatalog.PrimaryGesture(CommandId.ClipCopy)!.Value.Format(), Header(copy), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ClickingAnEntryDispatchesItOnTheRightClickedRow()
    {
        using var rig = await Rig.Opened();
        rig.RightClick(rig.Index("c.txt"));

        rig.Item(rig.View.FileMenu!, CommandId.ClipCopy).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        var seen = Assert.Single(rig.Seen);
        Assert.Equal(CommandId.ClipCopy, seen.Command);
        Assert.Equal(ContextTargetKind.File, seen.Target!.Kind);
        Assert.Equal(Path.Combine(rig.Directory, "c.txt"), seen.Target.EntryPath);
        Assert.NotNull(seen.Point);
        Assert.Null(rig.Workspace.ContextTarget);
        Assert.Null(rig.Workspace.ContextScreenPoint);
    }

    [AvaloniaFact]
    public async Task AFolderRowOffersTheFolderEntriesAndAMarkedSetReachesTheCommand()
    {
        using var rig = await Rig.Opened();
        var marked = new[] { Path.Combine(rig.Directory, "a.txt"), Path.Combine(rig.Directory, "b.txt") };
        rig.Left.List.PointerDown(rig.Index("a.txt"), FileListModifiers.Toggle);
        rig.Left.List.PointerDown(rig.Index("b.txt"), FileListModifiers.Toggle);
        rig.Left.Sync();
        rig.Settle();

        rig.RightClick(rig.Index("sub"));

        var menu = rig.View.FileMenu!;
        Assert.True(rig.Has(menu, CommandId.OpenInNewTab));
        Assert.True(rig.Has(menu, CommandId.OpenInOtherPanel));
        rig.Item(menu, CommandId.ClipCopy).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        var target = rig.Seen.Single().Target!;
        Assert.Equal(ContextTargetKind.Folder, target.Kind);
        Assert.Equal(marked, target.MarkedPaths);
    }

    [AvaloniaFact]
    public async Task EmptySpaceAndTheParentRowOpenTheEmptySpaceMenuWithoutMovingTheCursorOffTheParent()
    {
        using var rig = await Rig.Opened();
        rig.Workspace.ApplySelection(PanelSide.Left, rig.Index("a.txt"), []);
        rig.Settle();

        rig.RightClickEmptySpace();

        var menu = rig.View.FileMenu!;
        Assert.True(rig.Has(menu, CommandId.MkDir));
        Assert.True(rig.Has(menu, CommandId.Refresh));
        Assert.False(rig.Has(menu, CommandId.Open));
        rig.Item(menu, CommandId.Properties).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(ContextTargetKind.EmptySpace, rig.Seen.Single().Target!.Kind);
        Assert.Equal(rig.Directory, rig.Seen.Single().Target!.PanelFolder);

        rig.Seen.Clear();
        rig.RightClick(0);
        Assert.Equal(0, rig.Workspace.Read(PanelSide.Left).Cursor);
        Assert.True(rig.Has(rig.View.FileMenu!, CommandId.MkDir));
    }

    [AvaloniaFact]
    public async Task PasteIsOfferedOnlyWhenTheClipboardHoldsFiles()
    {
        using var rig = await Rig.Opened();
        rig.Workspace.ClipboardHasFiles = () => false;
        rig.RightClickEmptySpace();
        Assert.False(rig.Has(rig.View.FileMenu!, CommandId.ClipPaste));

        rig.Workspace.ClipboardHasFiles = () => true;
        rig.RightClickEmptySpace();
        Assert.True(rig.Has(rig.View.FileMenu!, CommandId.ClipPaste));
    }

    [AvaloniaFact]
    public async Task TheOpenWithSubmenuHoldsTheAssociationItemsThenTheOpenWithDialogCommand()
    {
        using var rig = await Rig.Opened();
        var ran = new List<string>();
        rig.Workspace.FileMenu = _ => [new ShellAction("Open with viewer", () => ran.Add("viewer"))];

        rig.RightClick(rig.Index("a.txt"));

        var submenu = rig.Item(rig.View.FileMenu!, CommandId.OpenWith);
        var children = submenu.Items.ToList();
        Assert.Equal("Open with viewer", Header(children[0]));
        Assert.IsType<Separator>(children[1]);
        Assert.Equal(rig.Label(CommandId.OpenWith), Header(children[^1]));
        Click(children[0]);
        Assert.Equal(["viewer"], ran);
        Click(children[^1]);
        Assert.Equal(CommandId.OpenWith, rig.Seen.Single().Command);

        rig.Workspace.FileMenu = _ => [];
        rig.RightClick(rig.Index("a.txt"));
        var bare = rig.Item(rig.View.FileMenu!, CommandId.OpenWith).Items.ToList();
        Assert.Equal(rig.Label(CommandId.OpenWith), Header(Assert.Single(bare)));
    }

    [AvaloniaFact]
    public async Task TheContextMenuKeyOpensTheMenuOfTheCursorRowAndNativeMenusOpenAtThatRow()
    {
        using var rig = await Rig.Opened();
        rig.Workspace.ApplySelection(PanelSide.Left, rig.Index("sub"), []);
        rig.Settle();
        Assert.Null(rig.View.FileMenu);

        Assert.True(rig.Shell.Dispatch(CommandId.ContextMenu));

        var menu = Assert.IsType<ContextMenu>(rig.View.FileMenu);
        Assert.True(rig.Has(menu, CommandId.OpenInNewTab));
        var point = rig.Workspace.MenuScreenPoint;
        Assert.Equal(rig.View.Files.RowScreenBottomLeft(rig.Index("sub")), point);
        Assert.NotNull(point);
    }

    [AvaloniaFact]
    public async Task ARightClickPointReachesACommandThatOpensANativeMenu()
    {
        using var rig = await Rig.Opened();
        PixelPoint? atDispatch = null;
        rig.RightClick(rig.Index("a.txt"));

        var entry = rig.Item(rig.View.FileMenu!, CommandId.Delete);
        rig.Shell.Handle(CommandId.Delete, () => atDispatch = rig.Workspace.MenuScreenPoint);
        entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.NotNull(atDispatch);
        Assert.Equal(rig.LastClickScreen, atDispatch);
    }

    private static string Header(object? item) => (string)((MenuItem)item!).Header!;

    private static void Click(object? item) => ((MenuItem)item!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

    private sealed class Rig : IDisposable
    {
        private readonly Window _window;
        private readonly ILocalizer _text;

        private Rig(string directory)
        {
            Directory = directory;
            _text = TextCatalog.Load(Language.En, Language.En);
            Workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker());
            Shell = new ShellContributions();
            Workspace.Bind(Shell);
            foreach (var command in new[] { CommandId.ClipCopy, CommandId.Properties, CommandId.OpenWith })
            {
                var id = command;
                Shell.Handle(id, () => Seen.Add((id, Workspace.ContextTarget, Workspace.ContextScreenPoint)));
            }

            Left = new PanelPaneModel(PanelSide.Left, Workspace, _text, new FakeDialogService(), Shell.Dispatch);
            View = (PanelPaneView)Left.CreateContent();
            _window = new Window { Width = 700, Height = 400, Content = View };
        }

        public string Directory { get; }

        public PanelWorkspace Workspace { get; }

        public ShellContributions Shell { get; }

        public PanelPaneModel Left { get; }

        public PanelPaneView View { get; }

        public List<(CommandId Command, PanelContextTarget? Target, PixelPoint? Point)> Seen { get; } = [];

        public PixelPoint? LastClickScreen { get; private set; }

        public static async Task<Rig> Opened()
        {
            var rig = new Rig(System.IO.Directory.CreateTempSubdirectory("d3sk-ctxmenu-").FullName);
            foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
            {
                File.WriteAllText(Path.Combine(rig.Directory, name), "x");
            }

            System.IO.Directory.CreateDirectory(Path.Combine(rig.Directory, "sub"));
            await rig.Workspace.OpenAsync(rig.Directory, rig.Directory, TestContext.Current.CancellationToken);
            rig._window.Show();
            rig.Settle();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (rig.Left.List.CellText(0, 0) is null && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            rig.Settle();
            return rig;
        }

        public string Label(CommandId command) => _text.Get(CommandCatalog.LabelKey(command));

        public int Index(string name)
        {
            var snap = Workspace.Read(PanelSide.Left);
            for (var i = 0; i < snap.Entries.Count; i++)
            {
                if (snap.Entries[i].Name == name)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Missing " + name);
        }

        public bool Has(ContextMenu menu, CommandId command) => Find(menu, command) is not null;

        public MenuItem Item(ContextMenu menu, CommandId command) =>
            Find(menu, command) ?? throw new InvalidOperationException(command + " is not in the menu");

        private MenuItem? Find(ContextMenu menu, CommandId command) =>
            menu.Items.OfType<MenuItem>().FirstOrDefault(item => Header(item).StartsWith(Label(command), StringComparison.Ordinal));

        public void RightClick(int index)
        {
            View.FileMenu?.Close();
            var at = View.Files.RowPoint(index, _window) ?? throw new InvalidOperationException("row " + index + " is not realized");
            Click(at);
        }

        public void RightClickEmptySpace()
        {
            View.FileMenu?.Close();
            var files = View.Files;
            Click(files.TranslatePoint(new Point(50, files.Bounds.Height - 6), _window)!.Value);
        }

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            _window.Close();
            Workspace.Dispose();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }
        }

        private void Click(Point at)
        {
            LastClickScreen = _window.PointToScreen(at);
            _window.MouseMove(at);
            _window.MouseDown(at, MouseButton.Right);
            _window.MouseUp(at, MouseButton.Right);
            Settle();
        }

        private sealed class Picker : IFolderPicker
        {
            public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(null);
        }
    }
}
