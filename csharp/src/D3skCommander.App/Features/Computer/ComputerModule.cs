using D3skCommander.App.Shell;
using D3skCommander.Core.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Computer;

/// <summary>Which mount prefixes go up to Computer. A test host replaces it to point at a temp folder.</summary>
public sealed record ComputerOptions(IReadOnlyList<string> RemovablePrefixes)
{
    public static ComputerOptions Default { get; } = new(PanelFileSystemProvider.DefaultPrefixes);
}

/// <summary>The Computer view's sources and options; the panel provider that serves it is built in the panels module.</summary>
public sealed class ComputerModule : IShellModule
{
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(ComputerOptions.Default);
        services.AddSingleton<IComputerSources, OsComputerSources>();
        services.AddSingleton<ComputerModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<ComputerModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
    }
}
