using Avalonia.Controls;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Features.Update;

/// <summary>The "Update to X" item on the right of the menu bar. Hidden until a check finds a newer release.</summary>
public sealed class UpdateMenuItem : IShellWidget
{
    private readonly Action _open;
    private MenuItem? _item;
    private string _header = "";

    public UpdateMenuItem(Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
    }

    public string? Name => "UpdateMenuItem";

    public bool IsShown => _item?.IsVisible ?? _header.Length > 0;

    public string Header => _header;

    public Control CreateContent()
    {
        var item = new MenuItem { Name = Name, Header = _header, IsVisible = _header.Length > 0 };
        item.Click += (_, _) => _open();
        _item = item;
        return item;
    }

    /// <summary>Shows <paramref name="header"/>, or hides the item on an empty text.</summary>
    public void Set(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = header;
        UiThread.Post(() =>
        {
            if (_item is not null)
            {
                _item.Header = header;
                _item.IsVisible = header.Length > 0;
            }
        });
    }
}
