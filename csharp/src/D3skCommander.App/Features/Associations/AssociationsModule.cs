using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Associations;

/// <summary>File associations: the service Enter, F3, F4 and the file context menu ask. EditFileAssociations belongs to the settings feature.</summary>
public static class AssociationsModule
{
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<FileAssociationService>();
        services.AddSingleton<IFileAssociations>(provider => provider.GetRequiredService<FileAssociationService>());
    }
}
