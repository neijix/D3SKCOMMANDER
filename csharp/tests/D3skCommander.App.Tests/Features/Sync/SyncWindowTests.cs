using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Sync;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Sync;
using Xunit;

namespace D3skCommander.App.Tests.Features.Sync;

public class SyncWindowTests
{
    [Fact]
    public void TheWindowOpensWithThePanelFolders()
    {
        using var dirs = new SyncDirs();
        var commands = new SyncCommands(
            new FakePanels(dirs.Left, dirs.Right),
            new JobService(),
            dirs.Text,
            new FakeFolderPicker(),
            new NoTools());

        var view = commands.CreateView();

        Assert.Equal(dirs.Left, view.LeftPath);
        Assert.Equal(dirs.Right, view.RightPath);
        Assert.Equal(SyncMode.Symmetric, view.Mode);
    }

    [Fact]
    public async Task BrowseFillsTheRoots()
    {
        using var dirs = new SyncDirs();
        var picker = new FakeFolderPicker();
        var view = dirs.View(new JobService(), picker);
        picker.Enqueue("/picked/left");
        picker.Enqueue("/picked/right");

        await view.BrowseLeftCommand.ExecuteAsync(null);
        await view.BrowseRightCommand.ExecuteAsync(null);

        Assert.Equal("/picked/left", view.LeftPath);
        Assert.Equal("/picked/right", view.RightPath);
    }

    [Fact]
    public void CompareListsLeftOnlyRightOnlyAndNewerItems()
    {
        using var dirs = new SyncDirs();
        var view = dirs.View(new JobService());

        dirs.Scan(view);

        Assert.Equal(["left.txt", "newer.txt", "right.txt"], view.Rows.Select(r => r.Item.Rel).OrderBy(n => n, StringComparer.Ordinal));
        var byName = view.Rows.ToDictionary(r => r.Item.Rel);
        Assert.Equal(SyncStatus.LeftOnly, byName["left.txt"].Item.Status);
        Assert.Equal(SyncStatus.RightOnly, byName["right.txt"].Item.Status);
        Assert.Equal(SyncStatus.LeftNewer, byName["newer.txt"].Item.Status);
        Assert.Equal(SyncAction.CopyToRight, byName["left.txt"].Action);
        Assert.Equal(SyncAction.CopyToLeft, byName["right.txt"].Action);
        Assert.Contains("Left only (1)", view.LeftOnlyText);
        Assert.Contains("Equal (1)", view.EqualText);
        Assert.Contains("3 actions", view.PendingLine);
        Assert.True(view.CanRun);
    }

    [Fact]
    public void TheClassFilterShowsAndHidesRows()
    {
        using var dirs = new SyncDirs();
        var view = dirs.View(new JobService());
        dirs.Scan(view);

        view.ShowEqual = true;
        Assert.Equal(4, view.Rows.Count);
        view.ShowLeftOnly = false;
        view.ShowRightOnly = false;
        view.ShowEqual = false;

        Assert.Equal(["newer.txt"], view.Rows.Select(r => r.Item.Rel));
    }

    [Fact]
    public void ChangingAnActionUpdatesThePendingCount()
    {
        using var dirs = new SyncDirs();
        var view = dirs.View(new JobService());
        dirs.Scan(view);
        var left = view.Rows.Single(r => r.Item.Rel == "left.txt");

        Assert.Equal(
            [SyncAction.None, SyncAction.CopyToRight, SyncAction.DeleteLeft],
            left.Options.Select(o => o.Action));
        left.Chosen = left.Options.Single(o => o.Action == SyncAction.DeleteLeft);

        Assert.Equal(SyncAction.DeleteLeft, left.Item.Action);
        Assert.Contains("3 actions", view.PendingLine);
        left.Chosen = left.Options[0];
        Assert.Contains("2 actions", view.PendingLine);
    }

    [Fact]
    public void RunCopiesAsPlannedAndReportsFailures()
    {
        using var dirs = new SyncDirs();
        var jobs = new JobService();
        var view = dirs.View(jobs);
        dirs.Scan(view);
        var newer = view.Rows.Single(r => r.Item.Rel == "newer.txt");
        newer.Chosen = newer.Options[0];

        view.Run();
        dirs.Until(view, () => view.ApplyJob is null && !view.IsBusy);

        Assert.True(File.Exists(Path.Combine(dirs.Right, "left.txt")));
        Assert.True(File.Exists(Path.Combine(dirs.Left, "right.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(dirs.Right, "newer.txt")));
        Assert.Contains("2 copied", view.StatusLine);
        Assert.False(view.HasFailures);
        Assert.Empty(view.Rows);

        File.Delete(Path.Combine(dirs.Left, "left.txt"));
        dirs.Scan(view);
        var gone = view.Rows.Single(r => r.Item.Rel == "right.txt" || r.Item.Rel == "left.txt");
        Assert.Equal(SyncStatus.RightOnly, gone.Item.Status);
    }

    [Fact]
    public void RunReportsAnItemThatFailed()
    {
        using var dirs = new SyncDirs();
        var view = dirs.View(new JobService());
        dirs.Scan(view);
        File.Delete(Path.Combine(dirs.Left, "left.txt"));

        view.Run();
        dirs.Until(view, () => view.ApplyJob is null && !view.IsBusy);

        Assert.True(view.HasFailures);
        Assert.Contains("left.txt", view.Failures[0]);
        Assert.Contains("failed", view.StatusLine);
    }

    [Fact]
    public void CancelStopsAScan()
    {
        using var dirs = new SyncDirs();
        var jobs = new GatedJobs(new JobService());
        var view = dirs.View(jobs);

        view.Compare();
        view.Cancel();
        jobs.Open();
        dirs.Until(view, () => !view.IsBusy);

        Assert.Empty(view.Rows);
        Assert.Equal(dirs.Text.Get("sync.cancelled"), view.StatusLine);
    }

    [Fact]
    public void ARightToLeftScanMirrorsTheRightSide()
    {
        using var dirs = new SyncDirs();
        var view = dirs.View(new JobService());
        view.Mode = SyncMode.RightToLeft;

        dirs.Scan(view);

        var byName = view.Rows.ToDictionary(r => r.Item.Rel);
        Assert.Equal(SyncAction.DeleteLeft, byName["left.txt"].Action);
        Assert.Equal(SyncAction.CopyToLeft, byName["right.txt"].Action);
        Assert.Equal(SyncAction.CopyToLeft, byName["newer.txt"].Action);
    }

    [AvaloniaFact]
    public void TheWindowShowsThePlan()
    {
        using var dirs = new SyncDirs();
        var view = dirs.View(new JobService());
        var window = new Window { Width = 980, Height = 600, Content = view.CreateContent() };
        window.Show();
        dirs.Scan(view);

        Assert.Equal(3, window.FindDescendantOfType<ListBox>()!.ItemCount);
        Assert.True(Shown(window, "RunButton"));
        Assert.Equal(dirs.Left, window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "LeftBox").Text);
        window.Close();
    }

    private static bool Shown(Window window, string name)
    {
        var found = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
        return found is not null && found.IsEffectivelyVisible;
    }

    /// <summary>Holds each single-task job at the gate, so a test can cancel it before its work starts.</summary>
    private sealed class GatedJobs : IJobService
    {
        private readonly JobService _inner;
        private readonly ManualResetEventSlim _gate = new(false);

        public GatedJobs(JobService inner) => _inner = inner;

        public IReadOnlyList<IJob> Jobs => _inner.Jobs;

        public void Open() => _gate.Set();

        public IJob Start(
            string title,
            IReadOnlyList<JobItem> items,
            Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> work) => _inner.Start(title, items, work);

        public IJob Start(string title, Func<IJobProgress, CancellationToken, Task> work) =>
            _inner.Start(title, (progress, token) =>
            {
                _gate.Wait();
                return work(progress, token);
            });

        public IJob Retry(IJob job, IReadOnlyList<string> itemNames) => _inner.Retry(job, itemNames);

        public IDisposable Subscribe(Action<JobCompleted> onCompleted) => _inner.Subscribe(onCompleted);
    }

    private sealed class NoTools : IToolWindowHost
    {
        public void Register(ToolKind kind, Func<IShellWidget> content)
        {
        }

        public ToolWindow Open(ToolKind kind) => throw new NotSupportedException();

        public bool IsOpen(ToolKind kind) => false;
    }

    private sealed class FakePanels : IPanelWorkspace
    {
        private readonly string _left;
        private readonly string _right;

        public FakePanels(string left, string right)
        {
            _left = left;
            _right = right;
        }

        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) =>
            PanelSnapshot.Empty with { Path = side == PanelSide.Left ? _left : _right };

        public PanelCapture? Capture() => null;

        public Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
