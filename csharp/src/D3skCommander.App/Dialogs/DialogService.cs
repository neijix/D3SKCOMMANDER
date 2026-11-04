namespace D3skCommander.App.Dialogs;

/// <summary>One modal at a time. The caller awaits its own answer; a second call waits until the first closes.</summary>
public sealed class DialogService : IDialogService
{
    private readonly IDialogSurface _surface;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DialogService(IDialogSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
    }

    public async Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        await Run(DialogRequest.ForMessage(title, message), cancellationToken);
    }

    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var result = await Run(DialogRequest.ForConfirm(title, message), cancellationToken);
        return result.Accepted;
    }

    public async Task<string?> AskTextAsync(
        string title,
        string label,
        string initialText = "",
        CancellationToken cancellationToken = default)
    {
        var result = await Run(DialogRequest.ForText(title, label, initialText), cancellationToken);
        return result.Accepted ? result.Text ?? "" : null;
    }

    public async Task<int?> ChooseAsync(
        string title,
        string message,
        IReadOnlyList<string> choices,
        CancellationToken cancellationToken = default)
    {
        var result = await Run(DialogRequest.ForChoice(title, message, choices), cancellationToken);
        return result.Accepted ? result.Choice : null;
    }

    public async Task<bool> ShowFormAsync(DialogForm form, CancellationToken cancellationToken = default)
    {
        var result = await Run(DialogRequest.ForForm(form), cancellationToken);
        return result.Accepted;
    }

    private async Task<DialogResult> Run(DialogRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _surface.ShowAsync(request, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
