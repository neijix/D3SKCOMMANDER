using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Checksums;
using Xunit;

namespace D3skCommander.App.Tests.Features.Tools;

public class ChecksumDialogTests
{
    [Fact]
    public async Task ChecksumCreateWritesOneListOrOneFileEach()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("a.txt", "alpha");
        await harness.AddFileAsync("b.txt", "beta");
        harness.Select(PanelSide.Left, "a.txt", "b.txt");

        var pending = harness.Commands.ChecksumCreateAsync();
        var shown = await dialogs.WaitShownAsync();
        var form = shown.Form!;
        ToolHarness.Draw(form);
        Assert.Equal(harness.Text.Get("cmd.checksum_create"), form.Title);
        Assert.Equal("2 " + harness.Text.Get("files"), form.Fields[0].Label);
        var choice = Assert.IsType<ChoiceDialogField>(form.Fields[1]);
        Assert.Equal(harness.Text.Get("dlg.algo"), choice.Label);
        Assert.Contains("CRC32", choice.Options);
        Assert.Contains("MD5", choice.Options);
        Assert.Contains("SHA-1", choice.Options);
        Assert.Contains("SHA-256", choice.Options);
        Assert.Equal("SHA-256", choice.Options[choice.SelectedIndex]);
        var perFile = Assert.IsType<CheckDialogField>(form.Fields[2]);
        Assert.Equal(harness.Text.Get("tools.one_per_file"), perFile.Label);
        var output = Assert.IsType<TextDialogField>(form.Fields[3]);
        Assert.Equal(Path.Combine(harness.Left, "CHECKSUM.sha256"), output.Text);
        choice.SelectedIndex = IndexOf(choice, "MD5");
        Assert.Equal(Path.Combine(harness.Left, "CHECKSUM.md5"), output.Text);
        perFile.IsChecked = true;
        Assert.Equal("", output.Text);
        Assert.True(form.CanAccept);
        dialogs.Answer(DialogResult.Ok());
        await pending;

        Assert.True(File.Exists(Path.Combine(harness.Left, "a.txt.md5")));
        Assert.True(File.Exists(Path.Combine(harness.Left, "b.txt.md5")));
        Assert.False(File.Exists(Path.Combine(harness.Left, "CHECKSUM.md5")));
        Assert.NotEmpty(harness.Jobs.Jobs);
    }

    [Fact]
    public async Task ChecksumCreateExpandsAFolder()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync(Path.Combine("sub", "a.txt"), "alpha");
        await harness.AddFileAsync(Path.Combine("sub", "b.txt"), "beta");
        harness.Select(PanelSide.Left, "sub");

        var pending = harness.Commands.ChecksumCreateAsync();
        var shown = await dialogs.WaitShownAsync();
        var form = shown.Form!;
        ToolHarness.Draw(form);
        Assert.Equal("2 " + harness.Text.Get("files"), form.Fields[0].Label);
        dialogs.Answer(DialogResult.Ok());
        await pending;

        var list = await File.ReadAllTextAsync(Path.Combine(harness.Left, "CHECKSUM.sha256"), TestContext.Current.CancellationToken);
        Assert.Contains("a.txt", list);
        Assert.Contains("b.txt", list);
    }

    [Fact]
    public async Task ChecksumVerifyReportsMatchAndMismatch()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("note.txt", "same");
        harness.Select(PanelSide.Left, "note.txt");
        var create = harness.Commands.ChecksumCreateAsync();
        var created = await dialogs.WaitShownAsync();
        ToolHarness.Draw(created.Form!);
        Assert.IsType<TextDialogField>(created.Form!.Fields[2]);
        dialogs.Answer(DialogResult.Ok());
        await create;

        harness.Select(PanelSide.Left, "note.txt.sha256");
        var verify = harness.Commands.ChecksumVerifyAsync();
        var matched = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, matched.Kind);
        Assert.Contains("1 " + harness.Text.Get("ok"), matched.Text);
        Assert.Contains("0 " + harness.Text.Get("tools.problems"), matched.Text);
        dialogs.Answer(DialogResult.Ok());
        await verify;

        await File.WriteAllTextAsync(Path.Combine(harness.Left, "note.txt"), "changed", TestContext.Current.CancellationToken);
        harness.Select(PanelSide.Left, "note.txt.sha256");
        verify = harness.Commands.ChecksumVerifyAsync();
        var differed = await dialogs.WaitShownAsync();
        Assert.Contains(harness.Text.Get("tools.differs"), differed.Text);
        dialogs.Answer(DialogResult.Ok());
        await verify;
    }

    [Fact]
    public async Task ChecksumVerifyRefusesAFileThatIsNotAList()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("note.txt", "same");
        harness.Select(PanelSide.Left, "note.txt");
        var pending = harness.Commands.ChecksumVerifyAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, shown.Kind);
        Assert.Equal(harness.Text.Get("tools.select_checksum"), shown.Text);
        dialogs.Answer(DialogResult.Ok());
        await pending;
        Assert.Empty(harness.Jobs.Jobs);
    }

    private static int IndexOf(ChoiceDialogField choice, string label)
    {
        for (var i = 0; i < choice.Options.Count; i++)
        {
            if (choice.Options[i] == label)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Missing " + label);
    }
}
