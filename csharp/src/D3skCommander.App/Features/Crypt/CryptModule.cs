using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Crypt;

public sealed class CryptModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<CryptModule>();
}
