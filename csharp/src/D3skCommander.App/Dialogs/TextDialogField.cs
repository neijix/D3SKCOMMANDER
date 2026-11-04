namespace D3skCommander.App.Dialogs;

/// <summary>A single-line text row. Pass <c>password: true</c> to mask the text.</summary>
public sealed class TextDialogField : DialogField
{
    private string _text;

    public TextDialogField(string label, string text = "", bool password = false)
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        IsPassword = password;
    }

    public bool IsPassword { get; }

    public char PasswordChar => IsPassword ? '•' : '\0';

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }
}
