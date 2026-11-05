using D3skCommander.App.Dialogs;
using D3skCommander.Core.Common;
using D3skCommander.Core.Ftp;

namespace D3skCommander.App.Features.Ftp;

public sealed partial class FtpViewModel
{
    private sealed record Secret(string Password, bool Store);

    private void LoadSites()
    {
        SiteNames.Clear();
        foreach (var site in _store.Current.Items)
        {
            SiteNames.Add(site.Name.Length > 0 ? site.Name : site.Host);
        }
    }

    private void SaveSites(List<FtpSite> items)
    {
        _store.Save(new FtpSites { Items = items });
        LoadSites();
    }

    private async Task AddSiteAsync()
    {
        var form = new FtpSiteForm(_text, null);
        if (!await _dialogs.ShowFormAsync(form.Build()).ConfigureAwait(true))
        {
            return;
        }

        var items = new List<FtpSite>(_store.Current.Items) { form.Read(null) };
        SaveSites(items);
        SelectedIndex = items.Count - 1;
    }

    private async Task EditSiteAsync()
    {
        var site = SelectedSite;
        if (site is null)
        {
            return;
        }

        var index = SelectedIndex;
        var form = new FtpSiteForm(_text, site);
        if (!await _dialogs.ShowFormAsync(form.Build()).ConfigureAwait(true))
        {
            return;
        }

        var items = new List<FtpSite>(_store.Current.Items);
        items[index] = form.Read(site);
        SaveSites(items);
        SelectedIndex = index;
    }

    private async Task DeleteSiteAsync()
    {
        var site = SelectedSite;
        if (site is null)
        {
            return;
        }

        var message = _text.Get("ftp.delete_confirm") + "\n" + site.Name;
        if (!await _dialogs.ConfirmAsync(_text.Get("ftp.delete_title"), message).ConfigureAwait(true))
        {
            return;
        }

        var items = new List<FtpSite>(_store.Current.Items);
        items.RemoveAt(SelectedIndex);
        SaveSites(items);
        SelectedIndex = -1;
    }

    private async Task ConnectAsync()
    {
        var site = SelectedSite;
        if (site is null || IsBusy)
        {
            return;
        }

        ErrorLine = "";
        var secret = await PasswordForAsync(site).ConfigureAwait(true);
        if (secret is null)
        {
            return;
        }

        IsBusy = true;
        Status = _text.Get("ftp.status.connecting");
        _connecting = new CancellationTokenSource();
        try
        {
            var connected = await _gateway.ConnectAsync(site, secret.Password, _connecting.Token).ConfigureAwait(true);
            if (!connected.IsSuccess)
            {
                Status = _text.Get("ftp.status.ready");
                Fail(connected.Error!);
                return;
            }

            _session = connected.Value;
            IsConnected = true;
            if (secret.Store)
            {
                await RememberAsync(site, secret.Password).ConfigureAwait(true);
            }

            await ShowStartFolderAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            _connecting.Dispose();
            _connecting = null;
        }
    }

    /// <summary>
    /// The password for a site: none for an anonymous login, else the vault's when the site keeps
    /// its password there, else asked through a masked field. Null when the user cancels.
    /// </summary>
    private async Task<Secret?> PasswordForAsync(FtpSite site)
    {
        if (site.User.Length == 0)
        {
            return new Secret("", false);
        }

        var vaulted = false;
        if (site.SavePassword)
        {
            var store = await _vault.UnlockAsync().ConfigureAwait(true);
            if (store is not null)
            {
                vaulted = true;
                var saved = store.Get(site.EffectivePasswordKey);
                if (saved is not null)
                {
                    return new Secret(saved, false);
                }
            }
        }

        var field = new TextDialogField(_text.Get("ftp.password"), "", password: true);
        var title = _text.Get("ftp.password_title");
        var form = new DialogForm(title, [new NoteDialogField(site.User + "@" + site.Host), field]);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            return null;
        }

        return new Secret(field.Text, vaulted);
    }

    private async Task RememberAsync(FtpSite site, string password)
    {
        var store = _vault.Current;
        if (store is null)
        {
            return;
        }

        store.Set(site.EffectivePasswordKey, password);
        var saved = await _vault.SaveAsync().ConfigureAwait(true);
        if (saved is not null)
        {
            Fail(saved);
        }
    }
}
