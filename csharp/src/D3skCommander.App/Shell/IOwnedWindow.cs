using Avalonia;
using Avalonia.Controls;

namespace D3skCommander.App.Shell;

/// <summary>A top-level window owned by the main window. Parent stays null: it is not a child control.</summary>
public interface IOwnedWindow
{
    string? Title { get; }

    bool IsVisible { get; }

    WindowBase? Owner { get; }

    StyledElement? Parent { get; }

    void Close();

    /// <summary>
    /// Asks the content whether the window may close, and closes it on yes. False means it stays open.
    /// Content with no opinion answers yes at once, so the task is already complete.
    /// </summary>
    Task<bool> TryCloseAsync();
}

/// <summary>Content that may refuse its window's close, asynchronously (for example an unsaved buffer).</summary>
public interface ICloseGuard
{
    /// <summary>True when the window may close. A refusal keeps the content as it is.</summary>
    Task<bool> CanCloseAsync();
}
