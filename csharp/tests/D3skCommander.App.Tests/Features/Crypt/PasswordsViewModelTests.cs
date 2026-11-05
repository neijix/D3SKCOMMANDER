using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Crypt;
using Xunit;

namespace D3skCommander.App.Tests.Features.Crypt;

public class PasswordsViewModelTests
{
    [AvaloniaFact]
    public async Task AddEditShowCopyAndRemoveAnEntry()
    {
        using var h = await CryptHarness.Open();
        var unlock = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f =>
        {
            var fields = f.Fields.OfType<TextDialogField>().ToList();
            fields[0].Text = "master";
            fields[1].Text = "master";
        });
        await unlock;
        var model = new PasswordsViewModel(h.Vault, h.Text);
        string? copied = null;
        model.CopyRequested += secret => copied = secret;
        Assert.True(model.IsEmpty);

        model.EntryName = "mail";
        model.Secret = "pw1";
        await model.SaveEntryAsync();
        Assert.Equal(["mail"], model.Keys);
        Assert.Equal("mail", model.Selected);
        Assert.Equal(h.Text.Get("pw.saved"), model.Status);

        model.Secret = "pw2";
        await model.SaveEntryAsync();
        Assert.Equal("pw2", h.Vault.Current!.Get("mail"));

        model.Selected = null;
        model.Selected = "mail";
        Assert.Equal('•', model.SecretChar);
        Assert.Equal("pw2", model.Secret);
        model.RevealCommand.Execute(null);
        Assert.Equal('\0', model.SecretChar);
        model.CopyCommand.Execute(null);
        Assert.Equal("pw2", copied);

        await model.RemoveEntryAsync();
        Assert.Empty(model.Keys);
        Assert.Null(h.Vault.Current!.Get("mail"));

        model.LockVaultCommand.Execute(null);
        Assert.False(model.IsUnlocked);
        Assert.Null(h.Vault.Current);
    }

    [AvaloniaFact]
    public async Task ASavedEntryIsOnDiskForTheNextSession()
    {
        using var h = await CryptHarness.Open();
        var unlock = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f =>
        {
            var fields = f.Fields.OfType<TextDialogField>().ToList();
            fields[0].Text = "m";
            fields[1].Text = "m";
        });
        await unlock;
        var model = new PasswordsViewModel(h.Vault, h.Text);
        model.EntryName = "site";
        model.Secret = "pw";
        await model.SaveEntryAsync();

        var next = h.NewVault().UnlockAsync();
        await h.AcceptFormAsync(f => f.Fields.OfType<TextDialogField>().First().Text = "m");
        Assert.Equal("pw", (await next)!.Get("site"));
    }

    [AvaloniaFact]
    public async Task TheViewRendersAndTheSecretIsMasked()
    {
        using var h = await CryptHarness.Open();
        var unlock = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f =>
        {
            var fields = f.Fields.OfType<TextDialogField>().ToList();
            fields[0].Text = "m";
            fields[1].Text = "m";
        });
        await unlock;
        var model = new PasswordsViewModel(h.Vault, h.Text);
        var window = new Avalonia.Controls.Window { Content = model.CreateContent() };
        window.Show();
        var view = Assert.IsType<PasswordsView>(window.Content);
        var box = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single(t => t.Name == "SecretBox");
        Assert.Equal('•', box.PasswordChar);
        window.Close();
    }
}
