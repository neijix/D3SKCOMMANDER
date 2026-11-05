using Avalonia.Controls;

namespace D3skCommander.App.Shell;

/// <summary>A piece of the shell. Features add these instead of editing the window.</summary>
public interface IShellWidget
{
    string? Name { get; }

    /// <summary>The ImGui drawing, kept until every view is ported.</summary>
    void Draw();

    /// <summary>The Avalonia content. A widget not yet ported shows as an empty placeholder carrying its name.</summary>
    Control CreateContent() => new Border { Name = Name };
}
