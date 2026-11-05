using System.Diagnostics;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>Starts the updated program and closes this one.</summary>
public interface IUpdateRestarter
{
    void Restart(Relaunch relaunch);
}

/// <summary>Starts the <see cref="Relaunch"/> as a separate process, then runs Quit.</summary>
public sealed class ProcessRestarter : IUpdateRestarter
{
    private readonly IShellContributions _shell;

    public ProcessRestarter(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    public void Restart(Relaunch relaunch)
    {
        ArgumentNullException.ThrowIfNull(relaunch);
        var info = new ProcessStartInfo(relaunch.Program) { UseShellExecute = false };
        foreach (var argument in relaunch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var started = Process.Start(info);
        _shell.Dispatch(CommandId.Quit);
    }
}
