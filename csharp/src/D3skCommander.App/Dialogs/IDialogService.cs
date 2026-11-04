namespace D3skCommander.App.Dialogs;

/// <summary>
/// Modal input. Each call waits until it is the only open dialog, then until the user answers.
/// A form is a <see cref="DialogForm"/> shown through the shared template.
/// </summary>
public interface IDialogService
{
    Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);

    Task<string?> AskTextAsync(string title, string label, string initialText = "", CancellationToken cancellationToken = default);

    /// <summary>The chosen index, or null when the user cancels.</summary>
    Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices, CancellationToken cancellationToken = default);

    /// <summary>True when the user accepts. The form's fields then hold what was entered.</summary>
    Task<bool> ShowFormAsync(DialogForm form, CancellationToken cancellationToken = default);
}
