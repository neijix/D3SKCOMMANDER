using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Ftp;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Ftp;

/// <summary>FTP and FTPS (Ctrl+N): the FTP tool window with the site list and the remote browser.</summary>
public sealed class FtpModule : IShellModule
{
    private readonly FtpCommands _commands;

    public FtpModule(FtpCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider => new FtpSiteStore(provider.GetRequiredService<AppSettings>().Directory));
        services.AddSingleton<IFtpGateway, FtpGateway>();
        services.AddSingleton<FtpViewModel>();
        services.AddSingleton<FtpCommands>();
        services.AddSingleton<FtpModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<FtpModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
