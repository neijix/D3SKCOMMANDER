using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Dialogs;

public sealed class DialogsModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<DialogsModule>();
}
