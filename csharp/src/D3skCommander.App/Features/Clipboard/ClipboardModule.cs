using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Clipboard;

public sealed class ClipboardModule
{
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IClipboardText, AvaloniaClipboardText>();
        services.AddSingleton<ClipboardModule>();
    }
}
