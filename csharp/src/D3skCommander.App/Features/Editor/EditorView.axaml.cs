using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace D3skCommander.App.Features.Editor;

/// <summary>
/// Draws <see cref="EditorViewModel"/>: find and replace bar, the AvaloniaEdit surface, status line.
/// Ctrl+S (Command on macOS) saves here; in the main window it stays the quick filter.
/// </summary>
public partial class EditorView : UserControl
{
    private EditorViewModel? _attached;

    public EditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        SaveButton.Click += (_, _) => _ = Model?.SaveAsync();
        NextButton.Click += (_, _) => Model?.FindNext();
        ReplaceButton.Click += (_, _) => Model?.ReplaceOne();
        ReplaceAllButton.Click += (_, _) => Model?.ReplaceAll();
        FindBox.KeyDown += OnFindKey;
        Surface.TextArea.SelectionChanged += (_, _) => Model?.SetSelection(Surface.SelectionStart, Surface.SelectionLength);
        DataContextChanged += (_, _) => Attach();
    }

    private EditorViewModel? Model => DataContext as EditorViewModel;

    private void Attach()
    {
        if (_attached is not null)
        {
            _attached.SelectionRequested -= OnSelectionRequested;
        }

        _attached = Model;
        if (_attached is not null)
        {
            _attached.SelectionRequested += OnSelectionRequested;
        }
    }

    private void OnSelectionRequested(int start, int length)
    {
        Surface.Select(start, length);
        Surface.TextArea.Caret.BringCaretToView();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var primary = e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta;
        if (e.Key == Key.S && primary)
        {
            e.Handled = true;
            _ = Model?.SaveAsync();
        }
    }

    private void OnFindKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Model?.FindNext();
        }
    }
}
