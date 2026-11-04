using D3skCommander.App.Dialogs;

namespace D3skCommander.App.Tests.Dialogs;

/// <summary>
/// The dialog service headless tests use. It is the real queue, answered here instead of by a window.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    private readonly FakeDialogSurface _surface = new();
    private readonly DialogService _dialogs;

    public FakeDialogService()
    {
        _dialogs = new DialogService(_surface);
    }

    public DialogRequest? Current => _surface.Current;

    public int ShownCount => _surface.ShownCount;

    public void Enqueue(DialogResult result) => _surface.Enqueue(result);

    public void Answer(DialogResult result) => _surface.Answer(result);

    public ValueTask<DialogRequest> WaitShownAsync() => _surface.WaitShownAsync();

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default) =>
        _dialogs.ShowMessageAsync(title, message, cancellationToken);

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default) =>
        _dialogs.ConfirmAsync(title, message, cancellationToken);

    public Task<string?> AskTextAsync(
        string title,
        string label,
        string initialText = "",
        CancellationToken cancellationToken = default) =>
        _dialogs.AskTextAsync(title, label, initialText, cancellationToken);

    public Task<int?> ChooseAsync(
        string title,
        string message,
        IReadOnlyList<string> choices,
        CancellationToken cancellationToken = default) =>
        _dialogs.ChooseAsync(title, message, choices, cancellationToken);

    public Task<bool> ShowFormAsync(DialogForm form, CancellationToken cancellationToken = default) =>
        _dialogs.ShowFormAsync(form, cancellationToken);
}
