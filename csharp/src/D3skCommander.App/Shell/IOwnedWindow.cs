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
}
