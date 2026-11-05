using Avalonia.Input.Platform;
using D3skCommander.App.Dialogs;

namespace D3skCommander.App.Features.Clipboard;

/// <summary>The clipboard of the open main window's top level.</summary>
public sealed class AvaloniaClipboardText : IClipboardText
{
    private readonly IDialogOwner _owner;

    public AvaloniaClipboardText(IDialogOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public async Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var clipboard = _owner.Current?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
        return true;
    }
}
