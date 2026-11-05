using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Crypt;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Crypt;

/// <summary>Encrypt, Decrypt and the password manager. Registers the vault unlock service other features share.</summary>
public sealed class CryptModule : IShellModule
{
    private readonly CryptCommands _commands;
    private readonly IToolWindowHost _tools;
    private readonly IPasswordVault _vault;
    private readonly ILocalizer _text;

    public CryptModule(CryptCommands commands, IToolWindowHost tools, IPasswordVault vault, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(text);
        _commands = commands;
        _tools = tools;
        _vault = vault;
        _text = text;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new CryptOptions());
        services.AddSingleton<IPasswordVault>(provider => new PasswordVault(
            Path.Combine(provider.GetRequiredService<AppSettings>().Directory, PasswordStore.FileName),
            provider.GetRequiredService<IDialogService>(),
            provider.GetRequiredService<ILocalizer>(),
            provider.GetRequiredService<CryptOptions>()));
        services.AddSingleton<CryptCommands>();
        services.AddSingleton<CryptModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<CryptModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
        _tools.Register(ToolKind.Passwords, () => new PasswordsViewModel(_vault, _text));
    }
}
