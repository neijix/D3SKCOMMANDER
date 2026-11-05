using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.Panels;

public class PanelWorkspaceTests
{
    [AvaloniaFact]
    public async Task EnterThenParentLeavesTheCursorOnThatFolder()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        var right = Path.Combine(root.Path, "right");
        Directory.CreateDirectory(Path.Combine(left, "sub"));
        File.WriteAllText(Path.Combine(left, "a.txt"), "x");
        Directory.CreateDirectory(right);
        using var workspace = await Open(left, right);

        var listed = workspace.Read(PanelSide.Left);
        Assert.Equal(0, listed.Selected);
        Assert.Equal(2, listed.Selectable);
        Assert.Equal("sub", listed.Entries[listed.Cursor].Name);
        workspace.ApplySelection(PanelSide.Left, At(listed, "sub"), []);
        await workspace.EnterAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Path.Combine(left, "sub"), workspace.Read(PanelSide.Left).Path);

        await workspace.ParentAsync(TestContext.Current.CancellationToken);
        var back = workspace.Read(PanelSide.Left);
        Assert.Equal(left, back.Path);
        Assert.Equal("sub", back.Entries[back.Cursor].Name);

        workspace.Switch();
        Assert.Equal(PanelSide.Right, workspace.ActiveSide);
    }

    [AvaloniaFact]
    public async Task AMissingFolderKeepsTheLocation()
    {
        using var root = new TempDir();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "x");
        using var workspace = await Open(root.Path, root.Path);
        var before = workspace.Read(PanelSide.Left).FailureToken;

        var ok = await workspace.GoAsync(
            PanelSide.Left,
            Path.Combine(root.Path, "missing"),
            TestContext.Current.CancellationToken);

        var after = workspace.Read(PanelSide.Left);
        Assert.False(ok);
        Assert.Equal(root.Path, after.Path);
        Assert.Equal(ErrorKind.NotFound, after.Error);
        Assert.True(after.FailureToken > before);
    }

    [AvaloniaFact]
    public async Task AClickKeepsTheListing()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        Directory.CreateDirectory(left);
        File.WriteAllText(Path.Combine(left, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(left, "sub"));
        using var workspace = await Open(left, left);

        var before = workspace.Read(PanelSide.Left);
        var file = At(before, "a.txt");
        Assert.NotEqual(file, before.Cursor);
        workspace.ApplySelection(PanelSide.Left, file, []);

        var after = workspace.Read(PanelSide.Left);
        Assert.Same(before.Entries, after.Entries);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(file, after.Cursor);
    }

    [AvaloniaFact]
    public async Task AChangedFolderIsRereadAfterTheInterval()
    {
        using var root = new TempDir();
        var clock = new ManualClock();
        using var workspace = await Open(root.Path, root.Path, clock);
        File.WriteAllText(Path.Combine(root.Path, "extra.txt"), "x");
        clock.Now += PanelWorkspace.RefreshInterval + TimeSpan.FromSeconds(1);

        await workspace.PollAsync(TestContext.Current.CancellationToken);

        Assert.Contains(workspace.Read(PanelSide.Left).Entries, entry => entry.Name == "extra.txt");
    }

    private static async Task<PanelWorkspace> Open(string left, string right, TimeProvider? clock = null)
    {
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker(), clock);
        await workspace.OpenAsync(left, right, TestContext.Current.CancellationToken);
        return workspace;
    }

    private static int At(PanelSnapshot snap, string name)
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

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("d3sk-panels-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
