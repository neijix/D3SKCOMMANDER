using D3skCommander.App.Shell;

namespace D3skCommander.App.ToolWindows;

/// <summary>Empty tool body used when a kind has no view yet.</summary>
public sealed class NamedWidget : IShellWidget
{
    public NamedWidget(string name) => Name = name;

    public string? Name { get; }

    public void Draw()
    {
    }
}
