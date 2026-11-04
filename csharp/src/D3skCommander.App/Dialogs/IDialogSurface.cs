namespace D3skCommander.App.Dialogs;

/// <summary>Shows one modal and waits for its answer. The service guarantees it is called one at a time.</summary>
public interface IDialogSurface
{
    Task<DialogResult> ShowAsync(DialogRequest request, CancellationToken cancellationToken);

    /// <summary>Draws the open modal. The fake surface used in tests draws nothing.</summary>
    void Draw();
}
