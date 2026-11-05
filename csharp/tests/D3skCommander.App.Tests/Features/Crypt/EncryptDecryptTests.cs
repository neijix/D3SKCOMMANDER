using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Features.Crypt;

public class EncryptDecryptTests
{
    private const string Plain = "the quick brown fox";

    [AvaloniaFact]
    public async Task EncryptThenDecryptRoundTripsThroughTheDialogs()
    {
        using var h = await CryptHarness.Open();
        await h.AddFileAsync("note.txt", Plain);
        h.SelectLeft("note.txt");

        var encrypt = h.Commands.EncryptAsync();
        var form = await h.AcceptFormAsync(f => Type(f, "s3cret", "s3cret"));
        await encrypt;

        var dest = Assert.IsType<FolderDialogField>(form.Fields[1]);
        Assert.Equal(h.Right, dest.Path);
        Assert.True(Assert.IsType<TextDialogField>(form.Fields[2]).IsPassword);
        Assert.True(Assert.IsType<TextDialogField>(form.Fields[3]).IsPassword);
        var encrypted = Path.Combine(h.Right, "note.txt.d3enc");
        Assert.True(File.Exists(encrypted));
        Assert.DoesNotContain(Plain, await File.ReadAllTextAsync(encrypted, TestContext.Current.CancellationToken));

        h.Workspace.Activate(PanelSide.Right);
        await h.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        var snap = h.Workspace.Read(PanelSide.Right);
        var index = snap.Entries.ToList().FindIndex(e => e.Name == "note.txt.d3enc");
        h.Workspace.ApplySelection(PanelSide.Right, index, [encrypted]);

        var decrypt = h.Commands.DecryptAsync();
        var back = await h.AcceptFormAsync(f => Type(f, "s3cret", null));
        var overwrite = await h.AcceptMessageAsync();
        await decrypt;

        Assert.Equal(h.Text.Get("crypt.overwrite"), overwrite.Text);

        Assert.Equal(3, back.Fields.Count);
        var restored = Path.Combine(h.Left, "note.txt");
        Assert.Equal(Plain, await File.ReadAllTextAsync(restored, TestContext.Current.CancellationToken));
        Assert.Equal(2, h.Jobs.Jobs.Count);
    }

    [AvaloniaFact]
    public async Task MismatchedPassphrasesAreRefused()
    {
        using var h = await CryptHarness.Open();
        await h.AddFileAsync("note.txt", Plain);
        h.SelectLeft("note.txt");

        var encrypt = h.Commands.EncryptAsync();
        var form = await h.AcceptFormAsync(f =>
        {
            Type(f, "one", "two");
            Assert.False(f.CanAccept);
        });
        var message = await h.AcceptMessageAsync();
        await encrypt;

        Assert.Equal(h.Text.Get("crypt.mismatch"), message.Text);
        Assert.Equal(2, form.Fields.Count(f => f is TextDialogField));
        Assert.Empty(Directory.GetFileSystemEntries(h.Right));
        Assert.Empty(h.Jobs.Jobs);
    }

    [AvaloniaFact]
    public async Task AWrongPasswordIsATypedErrorAndLeavesNoOutput()
    {
        using var h = await CryptHarness.Open();
        await h.AddFileAsync("note.txt", Plain);
        h.SelectLeft("note.txt");
        var encrypt = h.Commands.EncryptAsync();
        await h.AcceptFormAsync(f => Type(f, "right", "right"));
        await encrypt;

        var encrypted = Path.Combine(h.Right, "note.txt.d3enc");
        File.Move(encrypted, Path.Combine(h.Left, "note.txt.d3enc"));
        File.Delete(Path.Combine(h.Left, "note.txt"));
        await h.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        h.SelectLeft("note.txt.d3enc");

        var decrypt = h.Commands.DecryptAsync();
        await h.AcceptFormAsync(f => Type(f, "wrong", null));
        var message = await h.AcceptMessageAsync();
        await decrypt;

        Assert.Equal(h.Text.Get("crypt.wrong_password"), message.Text);
        Assert.False(File.Exists(Path.Combine(h.Right, "note.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(h.Right));
        var result = await h.Jobs.Jobs[^1].Completion;
        Assert.Equal(ErrorKind.AccessDenied, Assert.Single(result.Failed).Error.Kind);
    }

    [AvaloniaFact]
    public async Task AForeignFileIsShownThroughErrorLines()
    {
        using var h = await CryptHarness.Open();
        await h.AddFileAsync("fake.d3enc", "this was never encrypted by us");
        h.SelectLeft("fake.d3enc");

        var decrypt = h.Commands.DecryptAsync();
        await h.AcceptFormAsync(f => Type(f, "any", null));
        var message = await h.AcceptMessageAsync();
        await decrypt;

        var failed = Assert.Single((await h.Jobs.Jobs[^1].Completion).Failed);
        Assert.Equal(ErrorKind.InvalidData, failed.Error.Kind);
        Assert.Equal(ErrorLines.Describe(h.Text, failed.Error), message.Text);
        Assert.Empty(Directory.GetFileSystemEntries(h.Right));
    }

    [AvaloniaFact]
    public async Task WithoutASelectionAMessageSaysSo()
    {
        using var h = await CryptHarness.Open();
        h.Workspace.ApplySelection(PanelSide.Left, 0, []);

        var encrypt = h.Commands.EncryptAsync();
        var message = await h.AcceptMessageAsync();
        await encrypt;

        Assert.Equal(h.Text.Get("dlg.no_selection"), message.Text);
    }

    private static void Type(DialogForm form, string password, string? confirm)
    {
        Assert.IsType<TextDialogField>(form.Fields[2]).Text = password;
        if (confirm is not null)
        {
            Assert.IsType<TextDialogField>(form.Fields[3]).Text = confirm;
        }
    }
}
