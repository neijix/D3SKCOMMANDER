using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Index;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.QuickFind;

/// <summary>Quick find (Ctrl+Shift+F): the name index, its window and its progress line under the panels.</summary>
public sealed class QuickFindModule : IShellModule
{
    private readonly QuickFindCommands _commands;
    private readonly IndexService _index;
    private readonly ILocalizer _text;

    public QuickFindModule(QuickFindCommands commands, IndexService index, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(text);
        _commands = commands;
        _index = index;
        _text = text;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<AppSettings>();
            return new IndexService(
                NameIndexFile.PathIn(settings.Directory),
                () =>
                {
                    var current = settings.Preferences.Current;
                    return IndexOptions.From(current.IndexRoots, current.IndexExcludes);
                });
        });
        services.AddSingleton<QuickFindViewModel>();
        services.AddSingleton<QuickFindCommands>();
        services.AddSingleton<QuickFindModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<QuickFindModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
        shell.AddRegion(ShellRegion.UnderPanels, () => new IndexBarWidget(_index, _text));
    }
}
