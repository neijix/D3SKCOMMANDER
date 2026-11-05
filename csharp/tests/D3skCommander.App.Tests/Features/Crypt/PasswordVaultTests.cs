using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Crypt;
using D3skCommander.Core.Common;
using Xunit;

namespace D3skCommander.App.Tests.Features.Crypt;

public class PasswordVaultTests
{
    [AvaloniaFact]
    public async Task FirstUseCreatesTheVaultAskingTheMasterTwice()
    {
        using var h = await CryptHarness.Open();

        var unlock = h.Vault.UnlockAsync();
        var form = await h.AcceptFormAsync(f => Fill(f, "master", "master"));
        var store = await unlock;

        Assert.NotNull(store);
        Assert.True(File.Exists(h.VaultPath));
        Assert.Equal(3, form.Fields.Count);
        Assert.All(form.Fields.OfType<TextDialogField>(), field => Assert.True(field.IsPassword));
    }

    [AvaloniaFact]
    public async Task ADifferentConfirmationIsRefusedAndAsksAgain()
    {
        using var h = await CryptHarness.Open();

        var unlock = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f =>
        {
            Fill(f, "master", "other");
            Assert.False(f.CanAccept);
        });
        var message = await h.AcceptMessageAsync();
        await h.AcceptFormAsync(f => Fill(f, "master", "master"));

        Assert.Equal(h.Text.Get("crypt.mismatch"), message.Text);
        Assert.NotNull(await unlock);
    }

    [AvaloniaFact]
    public async Task AnEntrySavedIsReadBackAfterReUnlock()
    {
        using var h = await CryptHarness.Open();
        var create = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f => Fill(f, "master", "master"));
        var store = await create ?? throw new InvalidOperationException("Not unlocked.");
        store.Set("ftp.example", "hunter2");
        Assert.Null(await h.Vault.SaveAsync());

        var second = h.NewVault();
        var unlock = second.UnlockAsync();
        var form = await h.AcceptFormAsync(f => Fill(f, "master", null));
        var reopened = await unlock;

        Assert.Single(form.Fields);
        Assert.Equal("hunter2", reopened!.Get("ftp.example"));
    }

    [AvaloniaFact]
    public async Task AWrongMasterIsReportedThenAsksAgain()
    {
        using var h = await CryptHarness.Open();
        var create = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f => Fill(f, "master", "master"));
        await create;

        var unlock = h.NewVault().UnlockAsync();
        await h.AcceptFormAsync(f => Fill(f, "nope", null));
        var message = await h.AcceptMessageAsync();
        await h.AcceptFormAsync(f => Fill(f, "master", null));

        Assert.Equal(h.Text.Get("crypt.wrong_password"), message.Text);
        Assert.NotNull(await unlock);
    }

    [AvaloniaFact]
    public async Task TheStoreIsCachedUntilLocked()
    {
        using var h = await CryptHarness.Open();
        var first = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f => Fill(f, "master", "master"));
        var store = await first;
        var shown = h.Dialogs.ShownCount;

        Assert.Same(store, await h.Vault.UnlockAsync());
        Assert.Same(store, h.Vault.Current);
        Assert.Equal(shown, h.Dialogs.ShownCount);

        h.Vault.Lock();
        Assert.Null(h.Vault.Current);
        var again = h.Vault.UnlockAsync();
        await h.AcceptFormAsync(f => Fill(f, "master", null));
        Assert.NotSame(store, await again);
    }

    [AvaloniaFact]
    public async Task CancelingTheMasterLeavesItLocked()
    {
        using var h = await CryptHarness.Open();
        var unlock = h.Vault.UnlockAsync();
        var shown = await h.Dialogs.WaitShownAsync();
        Assert.NotNull(shown.Form);
        h.Dialogs.Answer(DialogResult.Cancel());

        Assert.Null(await unlock);
        Assert.False(File.Exists(h.VaultPath));
        Assert.Equal(ErrorKind.AccessDenied, (await h.Vault.SaveAsync())!.Kind);
    }

    private static void Fill(DialogForm form, string master, string? confirm)
    {
        var fields = form.Fields.OfType<TextDialogField>().ToList();
        fields[0].Text = master;
        if (confirm is not null)
        {
            fields[1].Text = confirm;
        }
    }
}
