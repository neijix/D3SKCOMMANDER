namespace D3skCommander.App.Shell;

/// <summary>A top-level window owned by the main window. Parent stays null: it is not a child control.</summary>
public interface IOwnedWindow
{
    string Title { get; }

    bool IsVisible { get; }

    MainWindow Owner { get; }

    object? Parent { get; }

    void Close();
}
