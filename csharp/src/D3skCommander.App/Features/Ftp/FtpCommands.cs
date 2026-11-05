using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Features.Ftp;

/// <summary>Ctrl+N opens the FTP window.</summary>
public sealed class FtpCommands
{
    private readonly IToolWindowHost _tools;
    private readonly FtpViewModel _window;

    public FtpCommands(IToolWindowHost tools, FtpViewModel window)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(window);
        _tools = tools;
        _window = window;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Ftp, Open);
        _tools.Register(ToolKind.Ftp, () => _window);
    }

    public void Open() => _tools.Open(ToolKind.Ftp);
}
