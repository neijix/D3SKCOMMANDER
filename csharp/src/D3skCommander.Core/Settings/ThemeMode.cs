using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Settings;

/// <summary>The window colours: follow the OS light/dark setting, or force one.</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>The command that picks each <see cref="ThemeMode"/> (the Show menu's three theme entries).</summary>
public static class ThemeModes
{
    public static IReadOnlyList<ThemeMode> All { get; } = Enum.GetValues<ThemeMode>();

    public static CommandId Command(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => CommandId.ThemeLight,
        ThemeMode.Dark => CommandId.ThemeDark,
        _ => CommandId.ThemeSystem,
    };

    /// <summary>The mode a theme command picks, or null for any other command.</summary>
    public static ThemeMode? FromCommand(CommandId command) => command switch
    {
        CommandId.ThemeSystem => ThemeMode.System,
        CommandId.ThemeLight => ThemeMode.Light,
        CommandId.ThemeDark => ThemeMode.Dark,
        _ => null,
    };
}
