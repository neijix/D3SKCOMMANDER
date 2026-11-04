using Hexa.NET.ImGui;

namespace D3skCommander.App.Shell;

/// <summary>A line of text in a shell region.</summary>
public sealed class LabelWidget : IShellWidget
{
    public LabelWidget(string name, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(text);
        Name = name;
        Text = text;
    }

    public string? Name { get; }

    public string Text { get; }

    public void Draw() => ImGui.TextUnformatted(Text);
}
