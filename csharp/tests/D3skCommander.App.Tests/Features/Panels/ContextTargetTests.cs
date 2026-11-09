using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Features.Drives;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.Panels;
using Xunit;
using static D3skCommander.App.Tests.Features.FileOps.FileOpsCommandTests;

namespace D3skCommander.App.Tests.Features.Panels;

public class ContextTargetTests
{
    [AvaloniaFact]
    public async Task OpenOnAFolderRowEntersItAndOnTheParentRowGoesUp()
    {
        using var root = new TempDir();
        var inner = Directory.CreateDirectory(Path.Combine(root.Path, "inner")).FullName;
        using var workspace = await Workspace(root.Path, root.Path);

        using (workspace.UseContextTarget(Target(ContextTargetKind.Folder, root.Path, inner)))
        {
            await workspace.OpenEntryAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(inner, workspace.Read(PanelSide.Left).Path);
        Assert.Null(workspace.ContextTarget);

        using (workspace.UseContextTarget(Target(ContextTargetKind.EmptySpace, inner, null)))
        {
            await workspace.OpenEntryAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(root.Path, workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task OpenOnAFileUsesTheFileOpenerAndTheMenuBarActsOnTheCursorEntry()
    {
        using var root = new TempDir();
        var file = Path.Combine(root.Path, "a.txt");
        File.WriteAllText(file, "x");
        var inner = Directory.CreateDirectory(Path.Combine(root.Path, "inner")).FullName;
        using var workspace = await Workspace(root.Path, root.Path);
        var opened = new List<string>();
        workspace.OpenFile = opened.Add;

        using (workspace.UseContextTarget(Target(ContextTargetKind.File, root.Path, file)))
        {
            await workspace.OpenEntryAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal([file], opened);

        var listed = workspace.Read(PanelSide.Left);
        workspace.ApplySelection(PanelSide.Left, At(listed, "inner"), []);
        await workspace.OpenEntryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(inner, workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task OpenInNewTabKeepsTheOldTabAndOpenInOtherPanelMovesTheOtherSide()
    {
        using var root = new TempDir();
        var inner = Directory.CreateDirectory(Path.Combine(root.Path, "inner")).FullName;
        var elsewhere = Directory.CreateDirectory(Path.Combine(root.Path, "elsewhere")).FullName;
        using var workspace = await Workspace(root.Path, elsewhere);

        using (workspace.UseContextTarget(Target(ContextTargetKind.Folder, root.Path, inner)))
        {
            await workspace.OpenInNewTabAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(inner, workspace.Read(PanelSide.Left).Path);
        workspace.CloseTab();
        Assert.Equal(root.Path, workspace.Read(PanelSide.Left).Path);

        using (workspace.UseContextTarget(Target(ContextTargetKind.Folder, root.Path, inner)))
        {
            await workspace.OpenInOtherPanelAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(inner, workspace.Read(PanelSide.Right).Path);
        Assert.Equal(root.Path, workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task AFolderThatVanishedShowsThePanelsNotFoundError()
    {
        using var root = new TempDir();
        var gone = Path.Combine(root.Path, "gone");
        using var workspace = await Workspace(root.Path, root.Path);
        var before = workspace.Read(PanelSide.Left).FailureToken;

        using (workspace.UseContextTarget(Target(ContextTargetKind.Folder, root.Path, gone)))
        {
            await workspace.OpenEntryAsync(TestContext.Current.CancellationToken);
            await workspace.OpenInNewTabAsync(TestContext.Current.CancellationToken);
        }

        var after = workspace.Read(PanelSide.Left);
        Assert.Equal(ErrorKind.NotFound, after.Error);
        Assert.True(after.FailureToken > before);
        Assert.Equal(root.Path, after.Path);
    }

    [AvaloniaFact]
    public async Task PasteIntoAFolderRowCopiesIntoThatFolderAndEmptySpaceIntoThePanelFolder()
    {
        using var root = new TempDir();
        var source = Directory.CreateDirectory(Path.Combine(root.Path, "source")).FullName;
        var inner = Directory.CreateDirectory(Path.Combine(source, "inner")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), "one");
        using var workspace = await Workspace(source, source);
        var commands = Commands(workspace, new ScriptedDialogs());
        var listed = workspace.Read(PanelSide.Left);
        workspace.ApplySelection(PanelSide.Left, At(listed, "a.txt"), []);
        await commands.ClipAsync(cut: false);

        using (workspace.UseContextTarget(Target(ContextTargetKind.Folder, source, inner)))
        {
            await commands.PasteAsync();
        }

        Assert.Equal("one", File.ReadAllText(Path.Combine(inner, "a.txt")));

        using (workspace.UseContextTarget(Target(ContextTargetKind.EmptySpace, source, null)))
        {
            await commands.PasteAsync();
        }

        Assert.Equal("one", File.ReadAllText(Path.Combine(source, "a (2).txt")));
    }

    [AvaloniaFact]
    public async Task CopyFromTheMenuTakesTheMarkedSetElseTheRowUnderTheClick()
    {
        using var root = new TempDir();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            File.WriteAllText(Path.Combine(root.Path, name), name);
        }

        using var workspace = await Workspace(root.Path, root.Path);
        var commands = Commands(workspace, new ScriptedDialogs());
        var a = Path.Combine(root.Path, "a.txt");
        var b = Path.Combine(root.Path, "b.txt");
        var c = Path.Combine(root.Path, "c.txt");

        using (workspace.UseContextTarget(Target(ContextTargetKind.File, root.Path, c, [a, b])))
        {
            await commands.ClipAsync(cut: false);
        }

        Assert.Equal([a, b], commands.Clipboard.Paths);

        using (workspace.UseContextTarget(Target(ContextTargetKind.File, root.Path, c)))
        {
            await commands.ClipAsync(cut: true);
        }

        Assert.Equal([c], commands.Clipboard.Paths);
    }

    [AvaloniaFact]
    public async Task PropertiesFromEmptySpaceShowsThePanelFolder()
    {
        using var harness = await DrivesHarness.Open();
        using var scope = harness.Workspace.UseContextTarget(Target(ContextTargetKind.EmptySpace, harness.Left, null));

        var pending = harness.Commands.PropertiesAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        var lines = shown.Form!.Fields.Select(field => field.Label).ToList();
        Assert.Contains("Type: Folder", lines);
        Assert.Contains("Name: left", lines);
        harness.Dialogs.Answer(DialogResult.Ok());
        await pending;
    }

    [Fact]
    public void TheNewCommandsAreHandledByThePanelWorkspace()
    {
        var shell = new ShellContributions();
        using var workspace = new PanelWorkspace(new D3skCommander.Core.Providers.LocalFileSystemProvider(), new IdlePicker());
        workspace.Bind(shell);

        foreach (var command in new[] { CommandId.Open, CommandId.OpenInNewTab, CommandId.OpenInOtherPanel })
        {
            Assert.True(shell.CanExecute(command), command.ToString());
        }
    }

    private static PanelContextTarget Target(ContextTargetKind kind, string folder, string? entry, params string[] marked) =>
        new(kind, PanelProviderKind.Local, folder, entry, marked);
}
