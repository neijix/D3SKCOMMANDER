using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Dialogs;

public sealed class DialogsModule
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<DialogsModule>();
        services.AddSingleton<IDialogOwner, ShellDialogOwner>();
        services.AddSingleton<IFolderPicker, OsFolderPicker>();
        services.AddSingleton<IDialogSurface, ImGuiDialogSurface>();
        services.AddSingleton<IDialogService, DialogService>();
    }
}
