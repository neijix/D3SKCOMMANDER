using Avalonia.Controls;

namespace D3skCommander.App.Shell;

/// <summary>A piece of the shell. Features add these instead of editing the window.</summary>
public interface IShellWidget
{
    string? Name { get; }

    /// <summary>The Avalonia content of the widget.</summary>
    Control CreateContent();
}
