namespace D3skCommander.App.Controls.FileList;

/// <summary>Modifiers for a file-list press. Toggle is Ctrl (Command on macOS).</summary>
[Flags]
public enum FileListModifiers
{
    None = 0,
    Shift = 1,
    Toggle = 2,
    Command = 4,
}

public enum FileListKey
{
    PageDown,
    PageUp,
    Down,
    Up,
    Left,
    Right,
    End,
    Home,
    Insert,
    Space,
    Enter,
    Escape,
}

internal static class FileListNavigation
{
    /// <summary>v0.1 moves a fixed 20 rows. Viewport paging is a later improvement.</summary>
    public const int PageStep = 20;
}
