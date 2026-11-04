using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.MultiRename;

public sealed class MultiRenameModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<MultiRenameModule>();
}
