using D3skCommander.Core.Commands;

namespace D3skCommander.App.Tests.Shell;

/// <summary>Commands the shell can run today: every command but the ones still listed here, which stay visible and disabled.</summary>
internal static class HandledCommands
{
    private static readonly HashSet<CommandId> NotYetHandled =
    [
        CommandId.DrivesLeft,
        CommandId.DrivesRight,
        CommandId.Ftp,
        CommandId.Properties,
        CommandId.QuickView,
    ];

    public static bool Handles(CommandId command) => !NotYetHandled.Contains(command);
}
