using Avalonia.Input;

namespace D3skCommander.App.Controls.FileList;

internal static class FileListNavigation
{
    /// <summary>v0.1 moves a fixed 20 rows. Viewport paging is a later improvement.</summary>
    public const int PageStep = 20;

    public static bool IsCommandChord(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control)
        || modifiers.HasFlag(KeyModifiers.Alt)
        || modifiers.HasFlag(KeyModifiers.Meta);

    public static bool IsToggle(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    public static bool IsNavigationKey(Key key) =>
        key == Key.PageDown
        || key == Key.PageUp
        || key == Key.Down
        || key == Key.Up
        || key == Key.Home
        || key == Key.End
        || key == Key.Insert
        || key == Key.Space;
}
