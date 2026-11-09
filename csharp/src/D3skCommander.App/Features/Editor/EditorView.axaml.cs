using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using D3skCommander.App.Features.QuickView;

namespace D3skCommander.App.Features.Editor;

/// <summary>
/// Draws <see cref="EditorViewModel"/>: a menu bar (File, Edit, Search, View), the find and replace bar, the
/// AvaloniaEdit surface coloured by the file's extension, and the status line. The editor's shortcuts
/// (Ctrl+S, Ctrl+D, F3…; Command on macOS) are taken here first; a typed key never reaches the main window's
/// key bindings, only the function keys and Ctrl/Alt chords the editor does not use.
/// </summary>
public partial class EditorView : UserControl
{
    private EditorViewModel? _attached;

    public EditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnTyped, RoutingStrategies.Bubble, handledEventsToo: false);
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
            _attached.PropertyChanged -= OnModelChanged;
        }

        _attached = Model;
        if (_attached is not null)
        {
            _attached.SelectionRequested += OnSelectionRequested;
            _attached.PropertyChanged += OnModelChanged;
            BuildMenu(_attached);
            Colour();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Path))
        {
            Colour();
        }
    }

    // The colouring follows the file's extension, as in the preview.
    private void Colour()
    {
        var path = Model?.Path ?? "";
        var wanted = path.Length == 0 ? null : PreviewViews.HighlightingFor(path);
        if (!ReferenceEquals(Surface.SyntaxHighlighting, wanted))
        {
            Surface.SyntaxHighlighting = wanted;
        }
    }

    private void OnSelectionRequested(int start, int length)
    {
        Surface.Select(start, length);
        Surface.TextArea.Caret.BringCaretToView();
        Surface.TextArea.Focus();
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (Shortcut(e.Key, e.KeyModifiers) is { } action)
        {
            e.Handled = true;
            action();
        }
    }

    // Typing keys stay in the editor: a letter, Enter or Delete must not run a panel command behind it.
    private void OnTyped(object? sender, KeyEventArgs e)
    {
        var chord = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;
        var function = e.Key is >= Key.F1 and <= Key.F24;
        if (!chord && !function && e.Key != Key.Escape)
        {
            e.Handled = true;
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
