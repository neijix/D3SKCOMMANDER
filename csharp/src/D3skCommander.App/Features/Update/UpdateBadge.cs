using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Update;

/// <summary>
/// The top-right corner of the menu bar: the running version, always, then an Update button that appears only when a
/// check finds a newer release (accent) or needs a sign-in. A click on either opens the Update window.
/// </summary>
public sealed class UpdateBadge : IShellWidget
{
    private readonly Action _open;
    private readonly string _version;
    private readonly string _versionTip;
    private Button? _button;
    private string _header = "";
    private bool _newer;

    public UpdateBadge(string version, string versionTip, Action open)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(versionTip);
        ArgumentNullException.ThrowIfNull(open);
        _version = version;
        _versionTip = versionTip;
        _open = open;
    }

    public string? Name => "UpdateBadge";

    /// <summary>True while the Update button is shown.</summary>
    public bool IsShown => _button?.IsVisible ?? _header.Length > 0;

    /// <summary>The Update button's text, empty while it is hidden.</summary>
    public string Header => _header;

    /// <summary>The version text, <c>v0.3.2</c>.</summary>
    public string VersionText => "v" + _version;

    public Control CreateContent()
    {
        var version = new Button
        {
            Name = "AppVersion",
            Content = VersionText,
            FontSize = 12,
            Opacity = 0.65,
            Padding = new Thickness(6, 2),
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(version, _versionTip);
        version.Click += (_, _) => _open();
        var button = new Button
        {
            Name = "UpdateButton",
            Content = _header,
            FontSize = 12,
            Padding = new Thickness(10, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = _header.Length > 0,
        };
        button.Classes.Set("accent", _newer);
        button.Click += (_, _) => _open();
        _button = button;
        return new StackPanel
        {
            Name = Name,
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Children = { version, button },
        };
    }

    /// <summary>Shows the button with <paramref name="header"/> (accent when <paramref name="newer"/>), or hides it on an empty text.</summary>
    public void Set(string header, bool newer = false)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = header;
        _newer = newer;
        UiThread.Post(() =>
        {
            if (_button is not null)
            {
                _button.Content = header;
                _button.IsVisible = header.Length > 0;
                _button.Classes.Set("accent", newer);
            }
        });
    }
}
