using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Archives;
using Xunit;

namespace D3skCommander.App.Tests.Features.Archives;

public class ArchiveCommandTests
{
    [AvaloniaTheory]
    [InlineData(1, "pack.zip", "zip")]
    [InlineData(3, "pack.7z", "7z")]
    public async Task PackWritesTheSelectionAndTheArchiveListsIt(int format, string name, string expected)
    {
        var dialogs = new FakeDialogService();
        using var harness = await ArchiveHarness.Open(dialogs);
        await harness.AddFileAsync(harness.Left, "a.txt", "alpha");
        await harness.AddFileAsync(harness.Left, "b.txt", "beta");
        harness.Select(PanelSide.Left, "a.txt", "b.txt");

        var pending = harness.Commands.PackAsync();
        var form = (await dialogs.WaitShownAsync()).Form!;
        Assert.Equal(harness.Text.Get("cmd.pack"), form.Title);
        var kind = Assert.IsType<ChoiceDialogField>(form.Fields[1]);
        var folder = Assert.IsType<FolderDialogField>(form.Fields[3]);
        var archiveName = Assert.IsType<TextDialogField>(form.Fields[4]);
        Assert.Equal(harness.Right, folder.Path);
        kind.SelectedIndex = format == 1 ? 0 : 3;
        Assert.EndsWith("." + expected, archiveName.Text, StringComparison.Ordinal);
        archiveName.Text = name;
        dialogs.Answer(DialogResult.Ok());
        await pending;

        var archive = Path.Combine(harness.Right, name);
        var listed = ArchiveIo.List(archive);
        Assert.True(listed.IsSuccess);
        var paths = listed.Value.Select(entry => entry.Path).Order().ToArray();
        Assert.Equal(["a.txt", "b.txt"], paths);
    }

    [AvaloniaFact]
    public async Task TheExtensionFollowsTheFormat()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ArchiveHarness.Open(dialogs);
        await harness.AddFileAsync(harness.Left, "notes.txt", "x");
        harness.Select(PanelSide.Left, "notes.txt");

        var pending = harness.Commands.PackAsync();
        var form = (await dialogs.WaitShownAsync()).Form!;
        var kind = Assert.IsType<ChoiceDialogField>(form.Fields[1]);
        var name = Assert.IsType<TextDialogField>(form.Fields[4]);
        Assert.Equal("notes.txt.zip", name.Text);
        kind.SelectedIndex = 2;
        Assert.Equal("notes.txt.tar.gz", name.Text);
        kind.SelectedIndex = 3;
        Assert.Equal("notes.txt.7z", name.Text);
        dialogs.Answer(DialogResult.Cancel());
        await pending;
        Assert.Empty(Directory.GetFiles(harness.Right));
    }

    [AvaloniaFact]
    public async Task UnpackExtractsIntoTheChosenFolder()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ArchiveHarness.Open(dialogs);
        await harness.AddFileAsync(harness.Left, "a.txt", "alpha");
        var archive = Path.Combine(harness.Left, "a.zip");
        Assert.True(ArchiveIo.Create(archive, ArchiveKind.Zip, [Path.Combine(harness.Left, "a.txt")], 6).IsSuccess);
        await harness.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        harness.Select(PanelSide.Left, "a.zip");

        var pending = harness.Commands.UnpackAsync();
        var shown = await dialogs.WaitShownAsync();
        var dest = Assert.IsType<FolderDialogField>(shown.Form!.Fields[1]);
        Assert.Equal(harness.Right, dest.Path);
        var picked = Path.Combine(harness.Root, "picked");
        Directory.CreateDirectory(picked);
        dest.Path = picked;
        dialogs.Answer(DialogResult.Ok());
        await pending;

        Assert.Equal("alpha", await File.ReadAllTextAsync(Path.Combine(picked, "a.txt"), TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task BackupZipsTheSelectionToADatedNameInTheOtherPanel()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ArchiveHarness.Open(dialogs);
        await harness.AddFileAsync(harness.Left, "notes.txt", "hello");
        harness.Select(PanelSide.Left, "notes.txt");

        await harness.Commands.BackupAsync();

        var zip = Assert.Single(Directory.GetFiles(harness.Right));
        Assert.Matches(new Regex(@"^notes\.txt-backup-\d{8}-\d{6}\.zip$"), Path.GetFileName(zip));
        Assert.Contains(ArchiveIo.List(zip).Value, entry => entry.Path == "notes.txt");
    }

    [AvaloniaFact]
    public async Task CancelLeavesNoPartialArchive()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ArchiveHarness.Open(dialogs);
        await harness.AddFileAsync(harness.Left, "a.txt", "alpha");
        harness.Select(PanelSide.Left, "a.txt");
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var pending = harness.Commands.PackAsync(cancel.Token);
        await dialogs.WaitShownAsync();
        dialogs.Answer(DialogResult.Ok());
        await pending;

        Assert.Empty(Directory.GetFileSystemEntries(harness.Right));
    }

    [AvaloniaFact]
    public async Task NothingSelectedShowsTheNoSelectionMessage()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ArchiveHarness.Open(dialogs);

        var pending = harness.Commands.PackAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(harness.Text.Get("dlg.no_selection"), shown.Text);
        dialogs.Answer(DialogResult.Ok());
        await pending;
    }
}
