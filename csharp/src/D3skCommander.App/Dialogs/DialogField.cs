using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace D3skCommander.App.Dialogs;

/// <summary>One row of a <see cref="DialogForm"/>. The shared template picks the editor from the field type.</summary>
public abstract class DialogField : INotifyPropertyChanged
{
    protected DialogField(string label)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        Label = label;
    }

    public string Label { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ValueChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
