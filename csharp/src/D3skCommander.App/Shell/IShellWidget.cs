namespace D3skCommander.App.Shell;

/// <summary>A piece of the shell drawn with ImGui. Features add these instead of editing the window.</summary>
public interface IShellWidget
{
    string? Name { get; }

    void Draw();
}
