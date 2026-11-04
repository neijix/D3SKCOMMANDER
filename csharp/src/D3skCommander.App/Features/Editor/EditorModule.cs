using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Editor;

public sealed class EditorModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<EditorModule>();
}
