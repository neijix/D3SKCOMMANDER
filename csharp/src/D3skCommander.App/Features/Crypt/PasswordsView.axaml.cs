using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace D3skCommander.App.Features.Crypt;

/// <summary>The password manager body over <see cref="PasswordsViewModel"/>. It places a copied secret on the clipboard.</summary>
public partial class PasswordsView : UserControl
{
    public PasswordsView()
    {
        InitializeComponent();
    }

    public PasswordsView(PasswordsViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.CopyRequested += OnCopy;
    }

    private async void OnCopy(string secret)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(secret);
        }
    }
}
