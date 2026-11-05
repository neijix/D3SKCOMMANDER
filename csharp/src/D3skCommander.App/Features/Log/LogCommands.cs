using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Features.Log;

/// <summary>The Log command opens the log tool window on a fresh read.</summary>
public sealed class LogCommands
{
    private readonly IToolWindowHost _tools;
    private readonly LogViewModel _model;

    public LogCommands(IToolWindowHost tools, LogViewModel model)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(model);
        _tools = tools;
        _model = model;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Log, Open);
        _tools.Register(ToolKind.Log, () => _model);
    }

    private void Open()
    {
        _model.Refresh();
        _tools.Open(ToolKind.Log);
    }
}
