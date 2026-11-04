using Hexa.NET.ImGui;

namespace D3skCommander.App.Shell;

/// <summary>An extra item on the right of the menu bar.</summary>
public sealed class MenuExtraWidget : IShellWidget
{
    public MenuExtraWidget(string name, string header)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(header);
        Name = name;
        Header = header;
    }

    public string? Name { get; }

    public string Header { get; }

    public void Draw() => ImGui.MenuItem(Header);
}
