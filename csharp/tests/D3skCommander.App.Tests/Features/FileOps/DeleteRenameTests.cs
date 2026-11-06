using Avalonia.Headless.XUnit;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using Xunit;
using Harness = D3skCommander.App.Tests.Features.FileOps.FileOpsCommandTests;

namespace D3skCommander.App.Tests.Features.FileOps;

public class DeleteRenameTests
{
    [AvaloniaFact]
    public async Task DeleteWithTrashOnSendsToTheTrashAfterConfirming()
    {
        var run = await Prepare(useTrash: true, confirm: true);
        await run.Commands.DeleteAsync();

        Assert.Equal([Path.Combine(run.Folder, "a.txt")], run.Trash.Sent);
        Assert.Equal("Delete 1 item(s)?", Assert.Single(run.Confirmed));
    }

    [AvaloniaFact]
    public async Task DeleteWithTrashOffRemovesTheFile()
    {
        var run = await Prepare(useTrash: false, confirm: true);
        await run.Commands.DeleteAsync();

        Assert.Empty(run.Trash.Sent);
        Assert.False(File.Exists(Path.Combine(run.Folder, "a.txt")));
        Assert.Single(run.Confirmed);
    }

    [AvaloniaFact]
    public async Task DeleteWithConfirmOffAsksNothing()
    {
        var run = await Prepare(useTrash: true, confirm: false);
        await run.Commands.DeleteAsync();

        Assert.Empty(run.Confirmed);
        Assert.Single(run.Trash.Sent);
    }

    [AvaloniaFact]
    public async Task DeleteDeclinedKeepsTheFile()
    {
        var run = await Prepare(useTrash: true, confirm: true);
        run.Dialogs.Confirm = false;
        await run.Commands.DeleteAsync();

        Assert.Empty(run.Trash.Sent);
        Assert.True(File.Exists(Path.Combine(run.Folder, "a.txt")));
    }

    [AvaloniaFact]
    public async Task DeletePermanentIgnoresTheTrashSettingAndAsksWhenConfirmIsOn()
    {
        var run = await Prepare(useTrash: true, confirm: true);
        await run.Commands.DeleteAsync(CommandId.DeletePermanent);

        Assert.Empty(run.Trash.Sent);
        Assert.False(File.Exists(Path.Combine(run.Folder, "a.txt")));
        Assert.Contains("Permanently delete 1 item(s)?", Assert.Single(run.Confirmed), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnItemThatCannotBeTrashedIsReported()
    {
        var run = await Prepare(useTrash: true, confirm: false);
        run.Trash.FailName = "a.txt";
        run.Dialogs.Choice = 1;
        await run.Commands.DeleteAsync();

        var report = Assert.Single(run.Dialogs.Asked);
        Assert.Equal("Some items failed", report.Title);
        Assert.Contains("a.txt", report.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(run.Folder, "a.txt")));
    }

    [AvaloniaFact]
    public async Task AMissingTrashOffersAPermanentDelete()
    {
        var run = await Prepare(useTrash: true, confirm: false);
        run.Trash.Unavailable = true;
        run.Dialogs.Answers.Enqueue(0);
        await run.Commands.DeleteAsync();

        var (_, message, choices) = Assert.Single(run.Dialogs.Asked);
        Assert.Contains("Cannot move to the trash", message, StringComparison.Ordinal);
        Assert.Equal(["Delete", "Delete, don't ask again"], choices);
        Assert.False(File.Exists(Path.Combine(run.Folder, "a.txt")));
        Assert.True(run.Settings.Preferences.Current.ConfirmTrashFallback);
    }

    [AvaloniaFact]
    public async Task CancellingTheMissingTrashPromptKeepsTheFile()
    {
        var run = await Prepare(useTrash: true, confirm: false);
        run.Trash.Unavailable = true;
        run.Dialogs.Answers.Enqueue(null);
        await run.Commands.DeleteAsync();

        Assert.Single(run.Dialogs.Asked);
        Assert.True(File.Exists(Path.Combine(run.Folder, "a.txt")));
    }

    [AvaloniaFact]
    public async Task DontAskAgainDeletesAndStopsAskingNextTime()
    {
        var run = await Prepare(useTrash: true, confirm: false);
        run.Trash.Unavailable = true;
        run.Dialogs.Answers.Enqueue(1);
        await run.Commands.DeleteAsync();

        Assert.False(File.Exists(Path.Combine(run.Folder, "a.txt")));
        Assert.False(run.Settings.Preferences.Current.ConfirmTrashFallback);

        File.WriteAllText(Path.Combine(run.Folder, "b.txt"), "y");
        await run.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        run.Workspace.ApplySelection(PanelSide.Left, Harness.At(run.Workspace.Read(PanelSide.Left), "b.txt"), []);
        await run.Commands.DeleteAsync();

        Assert.Single(run.Dialogs.Asked);
        Assert.False(File.Exists(Path.Combine(run.Folder, "b.txt")));
    }

    [AvaloniaFact]
    public async Task RenameCallsThroughAndRefreshes()
    {
        var run = await Prepare(useTrash: true, confirm: true);
        await run.Commands.RenameAsync(Path.Combine(run.Folder, "a.txt"), "b.txt");

        Assert.True(File.Exists(Path.Combine(run.Folder, "b.txt")));
        Assert.Contains(run.Workspace.Read(PanelSide.Left).Entries, entry => entry.Name == "b.txt");
        Assert.DoesNotContain(run.Workspace.Read(PanelSide.Left).Entries, entry => entry.Name == "a.txt");
    }

    [AvaloniaFact]
    public async Task RenameOntoAnExistingNameShowsTheTypedError()
    {
        var run = await Prepare(useTrash: true, confirm: true);
        File.WriteAllText(Path.Combine(run.Folder, "b.txt"), "other");
        await run.Commands.RenameAsync(Path.Combine(run.Folder, "a.txt"), "b.txt");

        Assert.True(File.Exists(Path.Combine(run.Folder, "a.txt")));
        Assert.Equal("other", File.ReadAllText(Path.Combine(run.Folder, "b.txt")));
        Assert.Single(run.Dialogs.Messages);
    }

    [AvaloniaFact]
    public void ConflictPromptShowsSizeAndDateOfBothFiles()
    {
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var when = new DateTimeOffset(2026, 11, 5, 10, 30, 0, TimeSpan.Zero);
        var body = ConflictPrompt.Body(
            text,
            new ConflictQuestion("/s/a", 2048, when, "/t/a", 10, null));

        Assert.Contains("2.0 KB", body, StringComparison.Ordinal);
        Assert.Contains("10 B", body, StringComparison.Ordinal);
        Assert.Contains("2026-11-05", body, StringComparison.Ordinal);
        Assert.Equal(7, ConflictPrompt.Choices(text).Length);
        Assert.Equal(ConflictChoice.OverwriteAllOlder, ConflictPrompt.Map(5));
        Assert.Equal(ConflictChoice.RenameNew, ConflictPrompt.Map(6));
    }

    private static async Task<Run> Prepare(bool useTrash, bool confirm)
    {
        var root = Directory.CreateTempSubdirectory("d3sk-del-").FullName;
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        var dialogs = new FileOpsCommandTests.ScriptedDialogs { Confirm = true };
        var trash = new FileOpsCommandTests.FakeTrash();
        var workspace = await Harness.Workspace(root, root);
        var settings = Harness.Settings(useTrash, confirm);
        var commands = Harness.Commands(workspace, dialogs, null, settings, trash);
        workspace.ApplySelection(PanelSide.Left, Harness.At(workspace.Read(PanelSide.Left), "a.txt"), []);
        return new Run(root, workspace, commands, dialogs, trash, settings);
    }

    private sealed record Run(
        string Folder,
        PanelWorkspace Workspace,
        FileOpsCommands Commands,
        FileOpsCommandTests.ScriptedDialogs Dialogs,
        FileOpsCommandTests.FakeTrash Trash,
        AppSettings Settings)
    {
        public IReadOnlyList<string> Confirmed => Dialogs.Confirmed;
    }
}
