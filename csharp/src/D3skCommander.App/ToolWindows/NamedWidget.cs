using Avalonia.Controls;
using D3skCommander.App.Shell;

namespace D3skCommander.App.ToolWindows;

/// <summary>Empty tool body used when a kind has no view.</summary>
public sealed class NamedWidget : IShellWidget
{
    public NamedWidget(string name) => Name = name;

    public string? Name { get; }

    public Control CreateContent() => new Border { Name = Name };
}
