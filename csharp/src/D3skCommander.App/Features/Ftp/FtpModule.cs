using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Ftp;

public sealed class FtpModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<FtpModule>();
}
