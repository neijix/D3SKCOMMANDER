using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.FileOps;

public sealed class FileOpsModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<FileOpsModule>();
}
