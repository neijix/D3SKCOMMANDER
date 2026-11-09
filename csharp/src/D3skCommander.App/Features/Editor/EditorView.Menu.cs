using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.Core.Localization;
using D3skCommander.Core.TextDocument;

namespace D3skCommander.App.Features.Editor;

/// <summary>
/// The editor's menu bar, laid out like the usual text editors: File (save, reload), Edit (undo, clipboard,
/// line commands, case), Search (find, replace, go to line) and View (word wrap, line numbers, zoom). Each
/// entry shows its shortcut; <see cref="Shortcut"/> runs the same action from the keyboard.
/// </summary>
public partial class EditorView
{
    private const double ZoomStep = 1;
    private double? _baseFontSize;

    /// <summary>The top entries of the menu bar, for tests.</summary>
    internal IReadOnlyList<MenuItem> MenuEntries => EditorMenu.Items.OfType<MenuItem>().ToList();

    private void BuildMenu(EditorViewModel model)
    {
        var text = model.Text;
        EditorMenu.Items.Clear();
        EditorMenu.Items.Add(Top(text, "editor.menu.file",
            Item(text, "editor.save", "Ctrl+S", () => _ = Model?.SaveAsync()),
            Item(text, "editor.reload", null, () => _ = Model?.ReloadAsync())));
        EditorMenu.Items.Add(Top(text, "editor.menu.edit",
            Item(text, "editor.undo", "Ctrl+Z", () => Surface.Undo()),
            Item(text, "editor.redo", "Ctrl+Y", () => Surface.Redo()),
            new Separator(),
            Item(text, "editor.cut", "Ctrl+X", () => Surface.Cut()),
            Item(text, "editor.copy", "Ctrl+C", () => Surface.Copy()),
            Item(text, "editor.paste", "Ctrl+V", () => Surface.Paste()),
            Item(text, "editor.delete", "Delete", () => Surface.Delete()),
            Item(text, "editor.select_all", "Ctrl+A", () => Surface.SelectAll()),
            new Separator(),
            Item(text, "editor.duplicate_line", "Ctrl+D", () => Change(LineEdits.Duplicate)),
            Item(text, "editor.delete_line", "Ctrl+Shift+L", () => Change(LineEdits.DeleteLines)),
            Item(text, "editor.move_up", "Ctrl+Shift+Up", () => Change((t, s, l) => LineEdits.MoveLines(t, s, l, up: true))),
            Item(text, "editor.move_down", "Ctrl+Shift+Down", () => Change((t, s, l) => LineEdits.MoveLines(t, s, l, up: false))),
            new Separator(),
            Item(text, "editor.upper", "Ctrl+Shift+U", () => Change((t, s, l) => LineEdits.ChangeCase(t, s, l, upper: true))),
            Item(text, "editor.lower", "Ctrl+U", () => Change((t, s, l) => LineEdits.ChangeCase(t, s, l, upper: false)))));
        EditorMenu.Items.Add(Top(text, "editor.menu.search",
            Item(text, "editor.find", "Ctrl+F", () => FocusBox(FindBox)),
            Item(text, "editor.find_next", "F3", () => Model?.FindNext()),
            Item(text, "editor.replace_one", "Ctrl+H", () => FocusBox(ReplaceBox)),
            Item(text, "editor.goto", "Ctrl+G", () => _ = Model?.GoToLineAsync())));
        EditorMenu.Items.Add(Top(text, "editor.menu.view",
            Check(text, "editor.word_wrap", () => Surface.WordWrap = !Surface.WordWrap, () => Surface.WordWrap),
            Check(text, "editor.line_numbers", () => Surface.ShowLineNumbers = !Surface.ShowLineNumbers, () => Surface.ShowLineNumbers),
            new Separator(),
            Item(text, "editor.zoom_in", "Ctrl++", () => Zoom(ZoomStep)),
            Item(text, "editor.zoom_out", "Ctrl+-", () => Zoom(-ZoomStep)),
            Item(text, "editor.zoom_reset", "Ctrl+0", () => Zoom(0))));
    }

    /// <summary>The action a key runs in the editor; null leaves the key to the editor surface or the window.</summary>
    private Action? Shortcut(Key key, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers = (modifiers & ~KeyModifiers.Meta) | KeyModifiers.Control;
        }

        return (key, modifiers) switch
        {
            (Key.S, KeyModifiers.Control) => () => _ = Model?.SaveAsync(),
            (Key.D, KeyModifiers.Control) => () => Change(LineEdits.Duplicate),
            (Key.L, KeyModifiers.Control | KeyModifiers.Shift) => () => Change(LineEdits.DeleteLines),
            (Key.Up, KeyModifiers.Control | KeyModifiers.Shift) => () => Change((t, s, l) => LineEdits.MoveLines(t, s, l, up: true)),
            (Key.Down, KeyModifiers.Control | KeyModifiers.Shift) => () => Change((t, s, l) => LineEdits.MoveLines(t, s, l, up: false)),
            (Key.U, KeyModifiers.Control | KeyModifiers.Shift) => () => Change((t, s, l) => LineEdits.ChangeCase(t, s, l, upper: true)),
            (Key.U, KeyModifiers.Control) => () => Change((t, s, l) => LineEdits.ChangeCase(t, s, l, upper: false)),
            (Key.F, KeyModifiers.Control) => () => FocusBox(FindBox),
            (Key.H, KeyModifiers.Control) => () => FocusBox(ReplaceBox),
            (Key.G, KeyModifiers.Control) => () => _ = Model?.GoToLineAsync(),
            (Key.F3, KeyModifiers.None) => () => Model?.FindNext(),
            (Key.OemPlus or Key.Add, KeyModifiers.Control) => () => Zoom(ZoomStep),
            (Key.OemMinus or Key.Subtract, KeyModifiers.Control) => () => Zoom(-ZoomStep),
            (Key.D0 or Key.NumPad0, KeyModifiers.Control) => () => Zoom(0),
            _ => null,
        };
    }

    /// <summary>Runs a line command on the caret or the selection, as one undo step.</summary>
    internal void Change(Func<string, int, int, LineEdit?> command)
    {
        if (Surface.IsReadOnly || Surface.Document is not { } document)
        {
            return;
        }

        if (command(document.Text, Surface.SelectionStart, Surface.SelectionLength) is not { } edit)
        {
            return;
        }

        document.Replace(edit.Offset, edit.Length, edit.Insert);
        Surface.Select(edit.SelectStart, edit.SelectLength);
        Surface.TextArea.Caret.BringCaretToView();
    }

    // The find or replace box gets the focus, with a one-line selection as the text to find.
    private void FocusBox(TextBox box)
    {
        var selected = Surface.SelectedText;
        if (Model is { } model && selected.Length > 0 && !selected.Contains('\n', StringComparison.Ordinal))
        {
            model.FindText = selected;
        }

        box.Focus();
        box.SelectAll();
    }

    private void Zoom(double step)
    {
        _baseFontSize ??= Surface.FontSize;
        Surface.FontSize = step == 0 ? _baseFontSize.Value : Math.Clamp(Surface.FontSize + step, 6, 72);
    }

    private static MenuItem Top(ILocalizer text, string key, params object[] items)
    {
        var top = new MenuItem { Header = text.Get(key) };
        foreach (var item in items)
        {
            top.Items.Add(item);
        }

        return top;
    }

    private static MenuItem Item(ILocalizer text, string key, string? gesture, Action run)
    {
        var item = new MenuItem { Header = text.Get(key) };
        if (gesture is not null)
        {
            item.InputGesture = KeyGesture.Parse(gesture.Replace("Ctrl++", "Ctrl+OemPlus", StringComparison.Ordinal).Replace("Ctrl+-", "Ctrl+OemMinus", StringComparison.Ordinal));
        }

        item.Click += (_, _) => run();
        return item;
    }

    // A check item: the click flips the setting, then the mark shows what the editor really has.
    private static MenuItem Check(ILocalizer text, string key, Action flip, Func<bool> state)
    {
        var item = new MenuItem { Header = text.Get(key), ToggleType = MenuItemToggleType.CheckBox, IsChecked = state() };
        item.Click += (_, _) =>
        {
            flip();
            Dispatcher.UIThread.Post(() => item.IsChecked = state());
        };
        return item;
    }
}
