using D3skCommander.App.Shell;
using D3skCommander.Core.FileOps;
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
        services.AddSingleton<FileOperationService>();
        services.AddSingleton<ITrash, SystemTrash>();
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
