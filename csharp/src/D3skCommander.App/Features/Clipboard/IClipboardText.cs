namespace D3skCommander.App.Features.Clipboard;

/// <summary>Puts plain text on the system clipboard. A test answers it with a fake.</summary>
public interface IClipboardText
{
    /// <summary>True when the text reached the clipboard.</summary>
    Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default);
}
