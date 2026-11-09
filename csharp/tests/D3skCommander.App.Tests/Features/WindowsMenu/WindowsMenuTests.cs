using Avalonia;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.WindowsMenu;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.WindowsMenu;

public class WindowsMenuTests
{
    [Fact]
    public void OffWindowsTheCommandSaysWindowsOnlyAndShowsNothing()
    {
        using var rig = new Rig { IsWindows = false };

        rig.Run();

        Assert.Empty(rig.Menu.Shown);
        Assert.Equal(["This command is available on Windows only"], rig.Status);
    }

    [Fact]
    public void AContextTargetsMarkedSetIsShownAtTheClickPointAndInvokedRefreshesThePanels()
    {
        using var rig = new Rig();
        rig.Workspace.Context = Target(ContextTargetKind.File, "C:\\d\\a.txt", "C:\\d\\a.txt", "C:\\d\\b.txt");
        rig.Module.ScreenPoint = () => new PixelPoint(120, 340);

        rig.Run();

        var shown = Assert.Single(rig.Menu.Shown);
        Assert.Equal(["C:\\d\\a.txt", "C:\\d\\b.txt"], shown.Paths);
        Assert.Equal(new PixelPoint(120, 340), shown.Point);
        Assert.Equal(1, rig.Workspace.Refreshes);
    }

    [Fact]
    public void WithoutAContextTargetTheCursorEntryIsShownAtTheMouse()
    {
        using var rig = new Rig();

        rig.Run();

        var shown = Assert.Single(rig.Menu.Shown);
        Assert.Equal(["C:\\d\\cursor.txt"], shown.Paths);
        Assert.Null(shown.Point);
    }

    [Fact]
    public void ADismissedMenuChangesNothing()
    {
        using var rig = new Rig();
        rig.Menu.Result = ExplorerMenuResult.Dismissed;

        rig.Run();

        Assert.Equal(0, rig.Workspace.Refreshes);
        Assert.Equal(0, rig.Dialogs.ShownCount);
        Assert.Empty(rig.Status);
    }

    [Fact]
    public async Task AFailedMenuShowsAnErrorDialogWithTheDetailAndDoesNotRefresh()
    {
        using var rig = new Rig();
        rig.Menu.Result = ExplorerMenuResult.Failed(ExplorerMenuFailure.Shell, "GetUIObjectOf 0x80004005");

        rig.Run();

        var request = await rig.Dialogs.WaitShownAsync();
        Assert.StartsWith("Windows refused to build the menu", request.Text, StringComparison.Ordinal);
        Assert.Contains("0x80004005", request.Text);
        Assert.Equal(0, rig.Workspace.Refreshes);
    }

    [Theory]
    [InlineData(PanelProviderKind.Archive)]
    [InlineData(PanelProviderKind.Remote)]
    public void FromTheMenuBarAnArchiveOrRemotePanelShowsAStatusAndNoMenu(PanelProviderKind provider)
    {
        using var rig = new Rig();
        rig.Workspace.Active = provider;

        rig.Run();

        Assert.Empty(rig.Menu.Shown);
        Assert.Equal(["Not available for this panel (archive or remote folder)"], rig.Status);
    }

    [Fact]
    public void AFlatListPanelStillShowsTheMenuFromTheMenuBar()
    {
        using var rig = new Rig();
        rig.Workspace.Active = PanelProviderKind.FlatList;

        rig.Run();

        Assert.Single(rig.Menu.Shown);
    }

    [Theory]
    [InlineData(PanelProviderKind.Archive)]
    [InlineData(PanelProviderKind.Remote)]
    public void AContextTargetOfAnArchiveOrRemotePanelShowsAStatusAndNoMenu(PanelProviderKind provider)
    {
        using var rig = new Rig();
        rig.Workspace.Context = new PanelContextTarget(ContextTargetKind.File, provider, "C:\\d", "C:\\d\\a.txt", []);

        rig.Run();

        Assert.Empty(rig.Menu.Shown);
        Assert.Equal(["Not available for this panel (archive or remote folder)"], rig.Status);
    }

    [Fact]
    public void WithNoTargetAtAllTheCommandFailsClosedWithAStatus()
    {
        using var rig = new Rig();
        rig.Workspace.NoTarget = true;

        rig.Run();

        Assert.Empty(rig.Menu.Shown);
        Assert.Equal(["Not available for this panel (archive or remote folder)"], rig.Status);
    }

    [Fact]
    public void AnInvalidPathFailsWithATypedReasonInsteadOfThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Explorer menu exists on Windows only");

        var result = new WindowsExplorerMenu().Show(["bad\0path"], null, IntPtr.Zero);

        Assert.Equal(ExplorerMenuOutcome.Failed, result.Outcome);
        Assert.Equal(ExplorerMenuFailure.InvalidPath, result.Reason);
    }

    [Fact]
    public void TheCommandIsHandledAndTheExplorerMenuFailsPlainlyOffWindows()
    {
        Assert.Contains(CommandId.WindowsMenu, CommandCatalog.All);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = new WindowsExplorerMenu().Show(["/tmp/a"], null, IntPtr.Zero);
        Assert.Equal(ExplorerMenuOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void TheRealShellFillsTheMenuForAFileAndReleasesEverything()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "d3sk-winmenu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "a.txt");
            File.WriteAllText(file, "x");
            var second = Path.Combine(folder, "b.txt");
            File.WriteAllText(second, "y");
            var counts = new List<int>();
            ExplorerMenuResult? failure = null;

            // Shell extensions want an STA thread; the menu is built, never shown.
            var thread = new Thread(() =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    return;
                }

                foreach (var paths in new[] { new[] { file }, new[] { file, second }, new[] { folder } })
                {
                    using var session = ShellMenuSession.Open(paths, IntPtr.Zero, out failure);
                    counts.Add(session?.ItemCount ?? -1);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.All(counts, count => Assert.True(count > 0, "the shell filled no items"));
            Assert.Equal(3, counts.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ItemsInDifferentFoldersAreRefusedBeforeTheShellIsAsked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var session = ShellMenuSession.Open(["C:\\one\\a.txt", "C:\\two\\b.txt"], IntPtr.Zero, out var failure);

        Assert.Null(session);
        Assert.Equal(ExplorerMenuFailure.NotOneFolder, failure?.Reason);
    }

    private static PanelContextTarget Target(ContextTargetKind kind, string entry, params string[] marked) =>
        new(kind, PanelProviderKind.Local, "C:\\d", entry, marked);

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            Dialogs = new FakeDialogService();
            Module = new WindowsMenuModule(Workspace, Menu, new NoOwner(), Dialogs, text);
            Module.Contribute(new StatusShell(Status.Add));
        }

        public bool IsWindows
        {
            set => Module.IsWindows = () => value;
        }

        public FakeWorkspace Workspace { get; } = new();

        public FakeMenu Menu { get; } = new();

        public FakeDialogService Dialogs { get; }

        public WindowsMenuModule Module { get; }

        public List<string> Status { get; } = [];

        public void Run() => Module.Show();

        public void Dispose()
        {
        }
    }

    private sealed class FakeMenu : IExplorerMenu
    {
        public List<(IReadOnlyList<string> Paths, PixelPoint? Point)> Shown { get; } = [];

        public ExplorerMenuResult Result { get; set; } = ExplorerMenuResult.Invoked;

        public ExplorerMenuResult Show(IReadOnlyList<string> paths, PixelPoint? screenPoint, IntPtr ownerWindow)
        {
            Shown.Add((paths, screenPoint));
            return Result;
        }
    }

    private sealed class NoOwner : IDialogOwner
    {
        public MainWindow? Current => null;
    }

    private sealed class FakeWorkspace : IPanelWorkspace
    {
        public PanelContextTarget? Context { get; set; }

        public int Refreshes { get; private set; }

        public PanelContextTarget? ContextTarget => Context;

        public PanelProviderKind Active { get; set; } = PanelProviderKind.Local;

        public bool NoTarget { get; set; }

        public PanelContextTarget? ContextTargetFor(PanelSide side, bool emptySpace) =>
            NoTarget ? null : new(ContextTargetKind.EmptySpace, Active, "C:\\d", null, []);

        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) => throw new NotSupportedException();

        public PanelCapture? Capture() => new(["C:\\d\\cursor.txt"], "C:\\d", "C:\\e");

        public Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Refreshes++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Only the parts a module uses here: handlers and the status line.</summary>
    private sealed class StatusShell(Action<string> status) : IShellContributions
    {
        public void Handle(CommandId command, Action invoke)
        {
        }

        public bool CanExecute(CommandId command) => true;

        public bool Dispatch(CommandId command) => true;

        public void SetStatus(string text) => status(text);

        public void Checked(CommandId command, Func<bool> isChecked)
        {
        }

        public void AppendMenuItem(string menuKey, CommandId command)
        {
        }

        public void AppendMenuSeparator(string menuKey)
        {
        }

        public void AppendDynamicItems(string menuKey, Func<IReadOnlyList<ShellAction>> items)
        {
        }

        public void AddMenuBarTrailing(Func<IShellWidget> build)
        {
        }

        public void OnStartup(Action action)
        {
        }

        public void SetPanel(PanelSide side, Func<IShellWidget> build)
        {
        }

        public void AddRegion(ShellRegion region, Func<IShellWidget> build)
        {
        }
    }
}
