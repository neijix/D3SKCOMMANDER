namespace D3skCommander.App.Dialogs;

/// <summary>
/// A form dialog's view-model. One shared template draws every form; the fields say which editors.
/// The optional accept check disables OK while it returns false (for example an empty folder).
/// </summary>
public sealed class DialogForm
{
    private readonly Func<DialogForm, bool>? _canAccept;

    public DialogForm(string title, IReadOnlyList<DialogField> fields, Func<DialogForm, bool>? canAccept = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(fields);
        Title = title;
        Fields = Copy(fields);
        _canAccept = canAccept;
        foreach (var field in Fields)
        {
            field.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Title { get; }

    public IReadOnlyList<DialogField> Fields { get; }

    public bool CanAccept => _canAccept is null || _canAccept(this);

    public event EventHandler? Changed;

    private static IReadOnlyList<DialogField> Copy(IReadOnlyList<DialogField> fields)
    {
        var copy = new DialogField[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            copy[i] = fields[i] ?? throw new ArgumentException("A form field cannot be null.", nameof(fields));
        }

        return copy;
    }
}
