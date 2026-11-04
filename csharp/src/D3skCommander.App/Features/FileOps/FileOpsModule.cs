using D3skCommander.App.Shell;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.OperationLog;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.FileOps;

public sealed class FileOpsModule : IShellModule
{
    private readonly FileOpsCommands _commands;

    public FileOpsModule(FileOpsCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IOperationLog>(_ =>
        {
            // Settings (a later row) turn this off. Until then every operation is recorded
            // in memory and echoed to stderr, which is the log's sink before a settings folder.
            var log = new OperationLog();
            log.Enabled = true;
            return log;
        });
        services.AddSingleton<FileOperationService>();
        services.AddSingleton<FileOpsCommands>();
        services.AddSingleton<FileOpsModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<FileOpsModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
