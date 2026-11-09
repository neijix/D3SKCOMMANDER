using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Search;

/// <summary>Alt+F7 opens the search window, starting in the active panel's folder.</summary>
public sealed class SearchCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IToolWindowHost _tools;
    private readonly SearchViewModel _window;

    public SearchCommands(IPanelWorkspace panels, IToolWindowHost tools, SearchViewModel window)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(window);
        _panels = panels;
        _tools = tools;
        _window = window;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Search, Open);
        _tools.Register(ToolKind.Search, () => _window);
    }

    public void Open()
    {
        var folder = _panels.Read(_panels.ActiveSide).Path;
        _window.Prepare(ComputerAddress.IsAddressOrUnder(folder) ? LocalFileSystem.HomeDirectory() : folder);
        _tools.Open(ToolKind.Search);
    }
}
