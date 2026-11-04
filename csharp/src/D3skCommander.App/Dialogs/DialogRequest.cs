namespace D3skCommander.App.Dialogs;

/// <summary>One modal waiting to be shown. Titles and labels are already localized by the caller.</summary>
public sealed class DialogRequest
{
    private DialogRequest(
        DialogKind kind,
        string title,
        string text,
        string initialText,
        IReadOnlyList<string> choices,
        DialogForm? form)
    {
        Kind = kind;
        Title = title;
        Text = text;
        InitialText = initialText;
        Choices = choices;
        Form = form;
    }

    public DialogKind Kind { get; }

    public string Title { get; }

    public string Text { get; }

    public string InitialText { get; }

    public IReadOnlyList<string> Choices { get; }

    public DialogForm? Form { get; }

    public static DialogRequest ForMessage(string title, string text) =>
        new(DialogKind.Message, Required(title, nameof(title)), RequiredText(text), "", [], null);

    public static DialogRequest ForConfirm(string title, string text) =>
        new(DialogKind.Confirm, Required(title, nameof(title)), RequiredText(text), "", [], null);

    public static DialogRequest ForText(string title, string label, string initialText)
    {
        ArgumentNullException.ThrowIfNull(initialText);
        return new DialogRequest(
            DialogKind.Text,
            Required(title, nameof(title)),
            Required(label, nameof(label)),
            initialText,
            [],
            null);
    }

    public static DialogRequest ForChoice(string title, string message, IReadOnlyList<string> choices) =>
        new(DialogKind.Choice, Required(title, nameof(title)), RequiredText(message), "", CopyChoices(choices), null);

    public static DialogRequest ForForm(DialogForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new DialogRequest(DialogKind.Form, form.Title, "", "", [], form);
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, name);
        return value;
    }

    private static string RequiredText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text;
    }

    private static IReadOnlyList<string> CopyChoices(IReadOnlyList<string> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
        {
            throw new ArgumentException("A choice dialog needs at least one choice.", nameof(choices));
        }

        var copy = new string[choices.Count];
        for (var i = 0; i < choices.Count; i++)
        {
            ArgumentException.ThrowIfNullOrEmpty(choices[i]);
            copy[i] = choices[i];
        }

        return copy;
    }
}
