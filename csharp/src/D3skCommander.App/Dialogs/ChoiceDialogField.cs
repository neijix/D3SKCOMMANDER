namespace D3skCommander.App.Dialogs;

/// <summary>A drop-down row. Options are already localized by the caller.</summary>
public sealed class ChoiceDialogField : DialogField
{
    private int _selectedIndex;

    public ChoiceDialogField(string label, IReadOnlyList<string> options, int selectedIndex = 0)
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0)
        {
            throw new ArgumentException("A choice field needs at least one option.", nameof(options));
        }

        for (var i = 0; i < options.Count; i++)
        {
            ArgumentException.ThrowIfNullOrEmpty(options[i]);
        }

        if (selectedIndex < 0 || selectedIndex >= options.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex), selectedIndex, "Choice index is outside the options.");
        }

        Options = options.ToArray();
        _selectedIndex = selectedIndex;
    }

    public IReadOnlyList<string> Options { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= Options.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Choice index is outside the options.");
            }

            Set(ref _selectedIndex, value);
        }
    }
}
