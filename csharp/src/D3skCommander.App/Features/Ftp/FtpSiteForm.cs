using D3skCommander.App.Dialogs;
using D3skCommander.Core.Ftp;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Ftp;

/// <summary>The add/edit dialog of one site. It never holds a password: that is the vault's or asked at connect.</summary>
public sealed class FtpSiteForm
{
    private readonly ILocalizer _text;
    private readonly TextDialogField _name;
    private readonly TextDialogField _host;
    private readonly TextDialogField _port;
    private readonly TextDialogField _user;
    private readonly CheckDialogField _secure;
    private readonly CheckDialogField _passive;
    private readonly TextDialogField _remoteDir;
    private readonly CheckDialogField _savePassword;
    private DialogForm? _form;

    public FtpSiteForm(ILocalizer text, FtpSite? site)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        var start = site ?? new FtpSite();
        _name = new TextDialogField(text.Get("ftp.site.name"), start.Name);
        _host = new TextDialogField(text.Get("ftp.site.host"), start.Host);
        _port = new TextDialogField(text.Get("ftp.site.port"), start.EffectivePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _user = new TextDialogField(text.Get("ftp.site.user"), start.User);
        _secure = new CheckDialogField(text.Get("ftp.site.secure"), start.Secure);
        _passive = new CheckDialogField(text.Get("ftp.site.passive"), start.Passive);
        _remoteDir = new TextDialogField(text.Get("ftp.site.remote_dir"), start.RemoteDir);
        _savePassword = new CheckDialogField(text.Get("ftp.site.save_password"), start.SavePassword);
    }

    public DialogForm Build()
    {
        _form ??= new DialogForm(
            _text.Get("ftp.site.title"),
            [_name, _host, _port, _user, _secure, _passive, _remoteDir, _savePassword, new NoteDialogField(_text.Get("ftp.site.hint"))],
            _ => _host.Text.Trim().Length > 0 && PortOf(_port.Text) is not null);
        return _form;
    }

    /// <summary>The site the fields describe. The name defaults to the host.</summary>
    public FtpSite Read(FtpSite? original)
    {
        var host = _host.Text.Trim();
        var name = _name.Text.Trim();
        return new FtpSite
        {
            Name = name.Length > 0 ? name : host,
            Host = host,
            Port = PortOf(_port.Text) ?? FtpSite.DefaultPort,
            User = _user.Text.Trim(),
            Secure = _secure.IsChecked,
            Passive = _passive.IsChecked,
            RemoteDir = _remoteDir.Text.Trim(),
            SavePassword = _savePassword.IsChecked,
            PasswordKey = original?.PasswordKey ?? "",
        };
    }

    /// <summary>1 to 65535, else null.</summary>
    public static int? PortOf(string text)
    {
        var parsed = int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port);
        return parsed && port >= 1 && port <= 65535 ? port : null;
    }
}
