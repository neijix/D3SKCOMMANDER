using Avalonia.Headless.XUnit;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using Xunit;
using static D3skCommander.App.Tests.Features.FileOps.FileOpsCommandTests;

namespace D3skCommander.App.Tests.Features.FileOps;

public class FileOpsClipboardTests
{
    [AvaloniaFact]
    public async Task CopyThenPasteInTheSameFolderMakesNumberedCopies()
    {
        using var root = new TempDir();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "one");
        Directory.CreateDirectory(Path.Combine(root.Path, "dir"));
        using var workspace = await Workspace(root.Path, root.Path);
        var commands = Commands(workspace, new ScriptedDialogs());
        var listed = workspace.Read(PanelSide.Left);
        var paths = new[] { "a.txt", "dir" }.Select(name => listed.Entries[At(listed, name)].Path).ToList();
        workspace.ApplySelection(PanelSide.Left, At(listed, "a.txt"), paths);

        await commands.ClipAsync(cut: false);
        await commands.PasteAsync();
        await commands.PasteAsync();

        Assert.Equal("one", File.ReadAllText(Path.Combine(root.Path, "a (2).txt")));
        Assert.True(File.Exists(Path.Combine(root.Path, "a (3).txt")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "dir (2)")));
        Assert.Contains(workspace.Read(PanelSide.Left).Entries, entry => entry.Name == "a (3).txt");
    }

    [AvaloniaFact]
    public async Task CutThenPasteInTheOtherPanelMovesOnce()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        var right = Path.Combine(root.Path, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        File.WriteAllText(Path.Combine(left, "b.txt"), "b");
        using var workspace = await Workspace(left, right);
        var commands = Commands(workspace, new ScriptedDialogs());
        var listed = workspace.Read(PanelSide.Left);
        workspace.ApplySelection(PanelSide.Left, At(listed, "b.txt"), []);

        await commands.ClipAsync(cut: true);
        await commands.PasteAsync();
        Assert.True(File.Exists(Path.Combine(left, "b.txt")));
        Assert.False(commands.Clipboard.IsEmpty);

        workspace.Activate(PanelSide.Right);
        await commands.PasteAsync();

        Assert.False(File.Exists(Path.Combine(left, "b.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(right, "b.txt")));
        Assert.True(commands.Clipboard.IsEmpty);
    }
}
