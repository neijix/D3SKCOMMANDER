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
using D3skCommander.Core.Providers;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Features.Panels;

public class PanelPaneTests
{
    [AvaloniaFact]
    public async Task TheFirstShowStartsTheListing()
    {
        using var rig = Rig.Create(width: 900);
        Assert.Equal("", rig.Workspace.Read(PanelSide.Left).Path);

        rig.Show();
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Path.Length > 0);

        Assert.Equal(Core.FileSystem.LocalFileSystem.HomeDirectory(), rig.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task EachPaneHasTheBarButtonsAndAClickRunsThemOnThatSide()
    {
        using var rig = await Rig.Opened();
        CommandId[] shared = [CommandId.Parent, CommandId.Hotlist, CommandId.History, CommandId.NetConnect, CommandId.Ftp];
        Assert.Equal([CommandId.DrivesLeft, .. shared], rig.LeftView.BarButtons.Select(b => (CommandId)b.Tag!));
        Assert.Equal([CommandId.DrivesRight, .. shared], rig.RightView.BarButtons.Select(b => (CommandId)b.Tag!));
        Assert.All(rig.LeftView.BarButtons, b => Assert.True(b.Bounds.Width > 0));
        Assert.Equal("Directory hotlist (Ctrl+D)", ToolTip.GetTip(rig.LeftView.BarButtons[2]));
        Assert.Equal(PanelSide.Left, rig.Workspace.ActiveSide);

        rig.ClickControl(rig.RightView.BarButtons[2]);
        rig.ClickControl(rig.RightView.BarButtons[5]);

        Assert.Equal([(CommandId.Hotlist, PanelSide.Right), (CommandId.Ftp, PanelSide.Right)], rig.Dispatched);
    }

    [AvaloniaFact]
    public async Task APressActivatesTheSide()
    {
        using var rig = await Rig.Opened();
        Assert.Equal(PanelSide.Left, rig.Workspace.ActiveSide);

        rig.Click(rig.Right, rig.Index(PanelSide.Right, "b.txt"));

        Assert.Equal(PanelSide.Right, rig.Workspace.ActiveSide);
    }

    [AvaloniaFact]
    public async Task ANewListingShowsWithoutInputWithColumnsFittedToTheWidth()
    {
        using var rig = await Rig.Opened();
        var wide = rig.Left.List.ShownColumns;
        Assert.Equal(FileListColumnFlags.All, wide);

        var other = Path.Combine(rig.Directory, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "fresh.txt"), "x");
        await rig.Workspace.GoAsync(PanelSide.Left, other, TestContext.Current.CancellationToken);
        await rig.WaitUntil(() => rig.Left.List.CellText(1, 0) == "fresh");

        Assert.Equal(other, rig.Left.Path);

        rig.Window.Width = 330;
        rig.Settle();
        Assert.NotEqual(FileListColumnFlags.All, rig.Left.List.ShownColumns);
        Assert.True(rig.Left.List.ShownColumns.HasFlag(FileListColumnFlags.Size) || rig.Left.List.ShownColumns == FileListColumnFlags.None);
    }

    [AvaloniaFact]
    public async Task EnterInThePathBarGoesToTheTypedPath()
    {
        using var rig = await Rig.Opened();
        var target = Path.Combine(rig.Directory, "other");
        Directory.CreateDirectory(target);
        var box = rig.LeftView.FindControl<TextBox>("PathBox")!;

        box.Focus();
        box.Text = target;
        rig.Settle();
        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Path == target);

        Assert.Equal(PanelSide.Left, rig.Workspace.ActiveSide);
        Assert.Equal(rig.Directory, rig.Workspace.Read(PanelSide.Right).Path);
    }

    [AvaloniaFact]
    public async Task EnterOrADoubleClickOpensTheEntryOnlyOnTheActiveSideOutsideARename()
    {
        using var rig = await Rig.Opened();
        var folder = Path.Combine(rig.Directory, "sub");

        await rig.Right.OpenEntryAsync();
        Assert.Equal(rig.Directory, rig.Workspace.Read(PanelSide.Right).Path);

        rig.Left.List.CursorIndex = rig.Index(PanelSide.Left, "sub");
        rig.Left.Sync();
        Assert.True(rig.Left.List.BeginRename());
        await rig.Left.OpenEntryAsync();
        Assert.Equal(rig.Directory, rig.Workspace.Read(PanelSide.Left).Path);
        rig.Left.List.CancelRename();

        rig.LeftView.Focus();
        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Path == folder);

        var back = rig.Index(PanelSide.Right, "sub");
        rig.DoubleClick(rig.Right, back);
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Right).Path == folder);
        Assert.Equal(PanelSide.Right, rig.Workspace.ActiveSide);
    }

    [AvaloniaFact]
    public async Task NavigationKeysReachTheListUnlessCtrlOrSuperIsHeld()
    {
        using var rig = await Rig.Opened();
        rig.LeftView.Focus();
        var start = rig.Left.List.CursorIndex;

        rig.Key(Key.Down, RawInputModifiers.Control);
        Assert.Equal(start, rig.Left.List.CursorIndex);
        rig.Key(Key.Down, RawInputModifiers.Meta);
        Assert.Equal(start, rig.Left.List.CursorIndex);

        rig.Key(Key.Down);
        Assert.Equal(start + 1, rig.Left.List.CursorIndex);
        Assert.Equal(start + 1, rig.Workspace.Read(PanelSide.Left).Cursor);

        rig.Workspace.Activate(PanelSide.Right);
        rig.Key(Key.Down);
        Assert.Equal(start + 1, rig.Left.List.CursorIndex);
    }

    [AvaloniaFact]
    public async Task MarksArePushedToTheWorkspace()
    {
        using var rig = await Rig.Opened();
        var index = rig.Index(PanelSide.Left, "b.txt");

        rig.Click(rig.Left, index, RawInputModifiers.Control);

        var snap = rig.Workspace.Read(PanelSide.Left);
        Assert.Single(snap.Marked);
        Assert.EndsWith("b.txt", snap.Marked[0], StringComparison.Ordinal);
        Assert.Equal(index, snap.Cursor);
    }

    [AvaloniaFact]
    public async Task TheStatusTextCountsTheMarksAndTheActiveSideIsTinted()
    {
        using var rig = await Rig.Opened();
        rig.Click(rig.Left, rig.Index(PanelSide.Left, "b.txt"), RawInputModifiers.Control);

        Assert.Equal("1 / 4 selected", rig.Left.StatusText);
        Assert.Equal("0 / 4 selected", rig.Right.StatusText);
        Assert.Contains("active", rig.Status(rig.LeftView).Classes);
        Assert.DoesNotContain("active", rig.Status(rig.RightView).Classes);

        rig.Click(rig.Right, rig.Index(PanelSide.Right, "a.txt"));
        await rig.WaitUntil(() => !rig.Status(rig.LeftView).Classes.Contains("active"));
        Assert.DoesNotContain("active", rig.Status(rig.LeftView).Classes);
        Assert.Contains("active", rig.Status(rig.RightView).Classes);
    }

    [AvaloniaFact]
    public async Task AFolderFailureOpensThePanelsFolderMessageWithTheErrorAndThePath()
    {
        using var rig = await Rig.Opened();
        var missing = Path.Combine(rig.Directory, "missing");

        var ok = await rig.Workspace.GoAsync(PanelSide.Left, missing, TestContext.Current.CancellationToken);
        Assert.False(ok);
        await rig.WaitUntil(() => rig.Dialogs.Current is not null);

        var request = rig.Dialogs.Current!;
        Assert.Equal("Folder", request.Title);
        Assert.Contains(missing, request.Text, StringComparison.Ordinal);
        Assert.Equal(1, rig.Dialogs.ShownCount);
    }

    [AvaloniaFact]
    public async Task EnterOnAFileHandsItToTheFileOpener()
    {
        using var rig = await Rig.Opened();
        var opened = new List<string>();
        rig.Workspace.OpenFile = opened.Add;
        rig.Left.List.CursorIndex = Index(rig.Workspace.Read(PanelSide.Left), "b.txt");

        await rig.Left.OpenEntryAsync();

        Assert.Equal([Path.Combine(rig.Directory, "b.txt")], opened);
        Assert.Equal(rig.Directory, rig.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task ARenameStartsOnTheActivePaneAndCommitsThroughTheHook()
    {
        using var rig = await Rig.Opened();
        var renames = new List<(string Path, string Name)>();
        rig.Workspace.Commit = (path, name) =>
        {
            renames.Add((path, name));
            return Task.CompletedTask;
        };
        rig.Left.List.CursorIndex = Index(rig.Workspace.Read(PanelSide.Left), "a.txt");

        rig.Workspace.Begin();
        Assert.True(rig.Left.List.IsRenaming);
        Assert.False(rig.Right.List.IsRenaming);
        rig.Left.List.RenameText = "z.txt";
        rig.Left.List.CommitRename();

        await rig.WaitUntil(() => renames.Count == 1);
        Assert.Equal((Path.Combine(rig.Directory, "a.txt"), "z.txt"), renames[0]);
    }

    private static int Index(PanelSnapshot snap, string name)
    {
        for (var i = 0; i < snap.Entries.Count; i++)
        {
            if (snap.Entries[i].Name == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Missing " + name);
    }

    private sealed class Rig : IDisposable
    {
        private readonly Window _window;

        private Rig(string directory, double width)
        {
            Directory = directory;
            var text = TextCatalog.Load(Language.En, Language.En);
            Dialogs = new FakeDialogService();
            Workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker());
            Left = new PanelPaneModel(PanelSide.Left, Workspace, text, Dialogs, Record);
            Right = new PanelPaneModel(PanelSide.Right, Workspace, text, Dialogs, Record);
            LeftView = (PanelPaneView)Left.CreateContent();
            RightView = (PanelPaneView)Right.CreateContent();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            Grid.SetColumn(RightView, 1);
            grid.Children.Add(LeftView);
            grid.Children.Add(RightView);
            _window = new Window { Width = width, Height = 400, Content = grid };
        }

        public string Directory { get; }

        /// <summary>Commands the panes sent to the shell, with the side active when each was sent.</summary>
        public List<(CommandId Command, PanelSide Active)> Dispatched { get; } = [];

        public FakeDialogService Dialogs { get; }

        public PanelWorkspace Workspace { get; }

        public PanelPaneModel Left { get; }

        public PanelPaneModel Right { get; }

        public PanelPaneView LeftView { get; }

        public PanelPaneView RightView { get; }

        public Window Window => _window;

        public static Rig Create(double width = 1000) =>
            new(System.IO.Directory.CreateTempSubdirectory("d3sk-pane-").FullName, width);

        public static async Task<Rig> Opened(double width = 1000)
        {
            var rig = Create(width);
            File.WriteAllText(Path.Combine(rig.Directory, "a.txt"), "x");
            File.WriteAllText(Path.Combine(rig.Directory, "b.txt"), "x");
            File.WriteAllText(Path.Combine(rig.Directory, "c.txt"), "x");
            System.IO.Directory.CreateDirectory(Path.Combine(rig.Directory, "sub"));
            await rig.Workspace.OpenAsync(rig.Directory, rig.Directory, TestContext.Current.CancellationToken);
            rig.Show();
            await rig.WaitUntil(() => rig.Left.List.CellText(0, 0) is not null && rig.Right.List.CellText(0, 0) is not null);
            return rig;
        }

        public void Show()
        {
            _window.Show();
            Settle();
        }

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
        }

        public int Index(PanelSide side, string name)
        {
            var snap = Workspace.Read(side);
            for (var i = 0; i < snap.Entries.Count; i++)
            {
                if (snap.Entries[i].Name == name)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Missing " + name);
        }

        public void ClickControl(Control control)
        {
            var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), _window)
                ?? throw new InvalidOperationException("The control is not in the window.");
            _window.MouseMove(at);
            _window.MouseDown(at, MouseButton.Left);
            _window.MouseUp(at, MouseButton.Left);
            Settle();
        }

        public void Click(PanelPaneModel pane, int index, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var at = PointOf(pane, index);
            _window.MouseMove(at, modifiers);
            _window.MouseDown(at, MouseButton.Left, modifiers);
            _window.MouseUp(at, MouseButton.Left, modifiers);
            Settle();
        }

        public void DoubleClick(PanelPaneModel pane, int index)
        {
            var at = PointOf(pane, index);
            _window.MouseDown(at, MouseButton.Left);
            _window.MouseUp(at, MouseButton.Left);
            _window.MouseDown(at, MouseButton.Left);
            _window.MouseUp(at, MouseButton.Left);
            Settle();
        }

        public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            _window.KeyPress(key, modifiers, PhysicalKey.None, null);
            Settle();
        }

        public TextBlock Status(PanelPaneView view) => view.FindControl<TextBlock>("StatusText")!;

        public async Task WaitUntil(Func<bool> ready)
        {
            var start = DateTime.UtcNow;
            while (!ready())
            {
                if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
                {
                    throw new TimeoutException("The pane did not update.");
                }

                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Settle();
        }

        private bool Record(CommandId command)
        {
            Dispatched.Add((command, Workspace.ActiveSide));
            return true;
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

        private Point PointOf(PanelPaneModel pane, int index)
        {
            var view = pane == Left ? LeftView : RightView;
            return view.Files.RowPoint(index, _window) ?? throw new InvalidOperationException("row " + index + " is not realized");
        }

        private sealed class Picker : IFolderPicker
        {
            public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(null);
        }
    }
}
