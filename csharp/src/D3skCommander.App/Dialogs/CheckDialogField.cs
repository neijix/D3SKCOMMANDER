namespace D3skCommander.App.Dialogs;

/// <summary>A checkbox row. The label is the checkbox caption.</summary>
public sealed class CheckDialogField : DialogField
{
    private bool _isChecked;

    public CheckDialogField(string label, bool isChecked = false)
        : base(label)
    {
        _isChecked = isChecked;
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }
}
