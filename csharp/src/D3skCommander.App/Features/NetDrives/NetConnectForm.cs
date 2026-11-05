using D3skCommander.App.Dialogs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;

namespace D3skCommander.App.Features.NetDrives;

/// <summary>What the connect form asked for, or the explanation of why it cannot be tried.</summary>
public sealed record NetConnectRequest(
    string Address,
    Share? Share,
    Credentials? Credentials,
    string? Letter,
    string? Problem);

/// <summary>The connect dialog's fields, and the check of what was typed before anything runs.</summary>
public sealed class NetConnectForm
{
    private readonly ILocalizer _text;
    private readonly IReadOnlyList<string> _recent;
    private readonly ChoiceDialogField? _recentField;
    private readonly TextDialogField _address;
    private readonly TextDialogField _user;
    private readonly TextDialogField _domain;
    private readonly TextDialogField _password;
    private readonly TextDialogField? _letter;
    private DialogForm? _form;

    public NetConnectForm(ILocalizer text, IReadOnlyList<string> recent, bool driveLetter)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(recent);
        _text = text;
        _recent = recent.ToArray();
        _address = new TextDialogField(text.Get("net.address"));
        _user = new TextDialogField(text.Get("net.user"));
        _domain = new TextDialogField(text.Get("net.domain"));
        _password = new TextDialogField(text.Get("net.password"), "", password: true);
        if (driveLetter)
        {
            _letter = new TextDialogField(text.Get("net.letter"));
        }

        if (_recent.Count > 0)
        {
            var options = new List<string> { text.Get("net.recent_none") };
            options.AddRange(_recent);
            _recentField = new ChoiceDialogField(text.Get("net.recent"), options);
            _recentField.ValueChanged += (_, _) => PickRecent();
        }
    }

    /// <summary>The form to show. The same instance keeps what was typed when the dialog opens again.</summary>
    public DialogForm Build()
    {
        if (_form is not null)
        {
            return _form;
        }

        var fields = new List<DialogField>();
        if (_recentField is not null)
        {
            fields.Add(_recentField);
        }

        fields.Add(_address);
        fields.Add(_user);
        fields.Add(_domain);
        fields.Add(_password);
        if (_letter is not null)
        {
            fields.Add(_letter);
        }

        fields.Add(new NoteDialogField(_text.Get("net.hint")));
        _form = new DialogForm(_text.Get("cmd.net_connect"), fields, _ => _address.Text.Trim().Length > 0);
        return _form;
    }

    public NetConnectRequest Read()
    {
        var address = _address.Text.Trim();
        var parsed = NetDrive.Parse(address);
        if (!parsed.IsSuccess)
        {
            return Bad(address, _text.Get("net.bad_address") + "\n" + Describe(parsed.Error!.Message));
        }

        string? letter = null;
        if (_letter is not null)
        {
            var typed = NetDrive.DriveLetter(_letter.Text);
            if (!typed.IsSuccess)
            {
                return Bad(address, _text.Get("net.bad_letter") + ": " + _letter.Text.Trim());
            }

            letter = typed.Value.Letter;
        }

        var share = parsed.Value;
        var user = _user.Text.Trim();
        if (share.User is null && user.Length > 0)
        {
            share = share with { User = user };
        }

        var credentials = new Credentials(user.Length > 0 ? user : share.User ?? "", _domain.Text.Trim(), _password.Text);
        return new NetConnectRequest(address, share, credentials, letter, null);
    }

    private string Describe(string message)
    {
        if (message.StartsWith("unsupported protocol", StringComparison.Ordinal))
        {
            return _text.Get("net.bad_protocol");
        }

        if (message.StartsWith("missing share", StringComparison.Ordinal))
        {
            return _text.Get("net.missing_share");
        }

        return _text.Get("net.missing_server");
    }

    private static NetConnectRequest Bad(string address, string problem) => new(address, null, null, null, problem);

    private void PickRecent()
    {
        var index = _recentField!.SelectedIndex;
        if (index > 0 && index <= _recent.Count)
        {
            _address.Text = _recent[index - 1];
        }
    }
}
