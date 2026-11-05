using Avalonia.Headless.XUnit;
using D3skCommander.App;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Ftp;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.Ftp;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Ftp;

public sealed class FtpWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("d3sk-ftpwin-").FullName;
    private readonly FakeSession _session = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly FakeVault _vault = new();
    private readonly FtpSiteStore _store;
    private FakeGateway? _gateway;

    public FtpWindowTests()
    {
        _store = new FtpSiteStore(_dir);
        _session.Folders["/"] = [FakeSession.Dir("/", "pub"), FakeSession.Leaf("/", "a.txt", 3)];
        _session.Folders["/pub"] = [FakeSession.Leaf("/pub", "b.bin", 4)];
        _session.Files["/a.txt"] = [1, 2, 3];
        _session.Files["/pub/b.bin"] = [4, 5, 6, 7];
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static Localizer English() =>
        Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    private FtpViewModel Model(FtpPanels? panels = null)
    {
        _gateway ??= new FakeGateway(_session);
        return new FtpViewModel(English(), _store, _gateway, _vault, _dialogs, new JobService(), panels ?? new FtpPanels(_dir));
    }

    private void SeedSite(bool savePassword = false, string user = "me")
    {
        _store.Save(new FtpSites { Items = [new FtpSite { Name = "home", Host = "example.test", User = user, SavePassword = savePassword }] });
    }

    private static void Type(DialogForm form, string label, string text)
    {
        foreach (var field in form.Fields)
        {
            if (field is TextDialogField entry && entry.Label == label)
            {
                entry.Text = text;
            }
        }
    }

    [AvaloniaFact]
    public void ModuleRegistersTheCommandAndTheToolWindow()
    {
        using var provider = Program.BuildProvider(English());
        var module = provider.GetRequiredService<FtpModule>();
        var shell = new ShellContributions();
        module.Contribute(shell);
        Assert.True(shell.CanExecute(CommandId.Ftp));
    }

    [Fact]
    public async Task AddingASiteSavesItWithoutAnyPassword()
    {
        var model = Model();
        _dialogs.OnForm = form =>
        {
            Type(form, "Name", "work");
            Type(form, "Host", "ftp.example.test");
            Type(form, "Port", "2121");
            Type(form, "User (empty for anonymous)", "alice");
            return true;
        };
        await model.AddCommand.ExecuteAsync(null);
        _store.Flush();
        var site = Assert.Single(_store.Current.Items);
        Assert.Equal(("work", "ftp.example.test", 2121, "alice"), (site.Name, site.Host, site.Port, site.User));
        Assert.Equal(["work"], model.SiteNames);
        Assert.DoesNotContain("\"password\"", File.ReadAllText(Path.Combine(_dir, "sites.json")));
    }

    [Fact]
    public async Task EditAndDeleteChangeTheList()
    {
        SeedSite();
        var model = Model();
        model.SelectedIndex = 0;
        _dialogs.OnForm = form =>
        {
            Type(form, "Name", "renamed");
            return true;
        };
        await model.EditCommand.ExecuteAsync(null);
        Assert.Equal(["renamed"], model.SiteNames);
        Assert.Equal("example.test", _store.Current.Items[0].Host);
        await model.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(model.SiteNames);
        Assert.Empty(_store.Current.Items);
    }

    [Fact]
    public async Task ConnectAsksTheMaskedPasswordAndBrowses()
    {
        SeedSite();
        var model = Model();
        model.SelectedIndex = 0;
        TextDialogField? asked = null;
        _dialogs.OnForm = form =>
        {
            asked = form.Fields.OfType<TextDialogField>().Single();
            asked.Text = "hunter2";
            return true;
        };
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.True(asked!.IsPassword);
        Assert.Equal("hunter2", _gateway!.Password);
        Assert.True(model.IsConnected);
        Assert.Equal("/", model.RemotePath);
        Assert.Equal(["pub", "a.txt"], model.Rows.Select(r => r.Name));

        model.Remote.CursorIndex = 0;
        await model.OpenCursorAsync();
        Assert.Equal("/pub", model.RemotePath);
        Assert.Equal(["..", "b.bin"], model.Rows.Select(r => r.Name));
        model.Remote.CursorIndex = 0;
        await model.OpenCursorAsync();
        Assert.Equal("/", model.RemotePath);
        await model.GoToAsync("/pub");
        await model.GoUpAsync();
        Assert.Equal("/", model.RemotePath);
    }

    [Fact]
    public async Task ASavedPasswordComesFromTheVaultWithoutAsking()
    {
        SeedSite(savePassword: true);
        var site = _store.Current.Items[0];
        (await _vault.UnlockAsync())!.Set(site.EffectivePasswordKey, "from-vault");
        var model = Model();
        model.SelectedIndex = 0;
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("from-vault", _gateway!.Password);
        Assert.Empty(_dialogs.Forms);
    }

    [Fact]
    public async Task ATypedPasswordIsStoredInTheVaultWhenTheSiteKeepsIt()
    {
        SeedSite(savePassword: true);
        var model = Model();
        model.SelectedIndex = 0;
        _dialogs.OnForm = form =>
        {
            form.Fields.OfType<TextDialogField>().Single().Text = "typed";
            return true;
        };
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("typed", _vault.Current!.Get(_store.Current.Items[0].EffectivePasswordKey));
        Assert.Equal(1, _vault.Saves);
    }

    [Fact]
    public async Task ACancelledPasswordDoesNotConnect()
    {
        SeedSite();
        var model = Model();
        model.SelectedIndex = 0;
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.False(model.IsConnected);
        Assert.Null(_gateway!.Site);
    }

    [Fact]
    public async Task ARefusedCertificateIsShownWithItsReason()
    {
        SeedSite(user: "");
        _gateway = new FakeGateway(_session)
        {
            Refusal = OperationError.Network("server certificate refused: the certificate is not trusted (UntrustedRoot) (SHA-256 AB12)"),
        };
        var model = Model();
        model.SelectedIndex = 0;
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.False(model.IsConnected);
        Assert.Equal(
            "The server certificate was refused: the certificate is not trusted (UntrustedRoot) (SHA-256 AB12)",
            model.ErrorLine);
    }

    [Fact]
    public async Task DownloadCopiesTheMarkedEntriesIntoTheActivePanelFolder()
    {
        SeedSite(user: "");
        var local = Path.Combine(_dir, "local");
        Directory.CreateDirectory(local);
        var panels = new FtpPanels(local);
        var model = Model(panels);
        model.SelectedIndex = 0;
        await model.ConnectCommand.ExecuteAsync(null);
        model.Remote.CursorIndex = 0;
        model.Remote.CursorIndex = 1;
        var job = await model.DownloadAsync();
        Assert.NotNull(job);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(local, "a.txt")));
        Assert.Equal(1, panels.Refreshes);

        // The folder row downloads recursively.
        model.Remote.CursorIndex = 0;
        await model.DownloadAsync();
        Assert.Equal([4, 5, 6, 7], File.ReadAllBytes(Path.Combine(local, "pub", "b.bin")));
    }

    [Fact]
    public async Task UploadSendsTheActivePanelSelectionIntoTheRemoteFolder()
    {
        SeedSite(user: "");
        var local = Path.Combine(_dir, "up");
        Directory.CreateDirectory(Path.Combine(local, "tree"));
        File.WriteAllBytes(Path.Combine(local, "one.txt"), [9, 9]);
        File.WriteAllBytes(Path.Combine(local, "tree", "two.txt"), [8]);
        var panels = new FtpPanels(local, Path.Combine(local, "one.txt"), Path.Combine(local, "tree"));
        var model = Model(panels);
        model.SelectedIndex = 0;
        await model.ConnectCommand.ExecuteAsync(null);
        await model.GoToAsync("/pub");
        var job = await model.UploadAsync();
        Assert.NotNull(job);
        Assert.Equal(["/pub/one.txt", "/pub/tree/two.txt"], _session.Uploaded);
        Assert.Equal(["/pub/tree"], _session.Created);
        Assert.Equal(1, panels.Refreshes);
    }

    [Fact]
    public async Task ClosingTheWindowDisconnects()
    {
        SeedSite(user: "");
        var model = Model();
        model.SelectedIndex = 0;
        await model.ConnectCommand.ExecuteAsync(null);
        Assert.True(await model.CanCloseAsync());
        Assert.True(_session.Disposed);
        Assert.False(model.IsConnected);
    }

    [AvaloniaFact]
    public async Task TheViewShowsTheRemoteRowsInTheSharedList()
    {
        SeedSite(user: "");
        var model = Model();
        model.SelectedIndex = 0;
        await model.ConnectCommand.ExecuteAsync(null);
        var view = model.CreateView();
        var window = new Avalonia.Controls.Window { Content = view, Width = 800, Height = 500 };
        window.Show();
        Assert.Equal("pub", model.Remote.CellText(0, 0));
        Assert.Equal("a", model.Remote.CellText(1, 0));
        window.Close();
    }
}
