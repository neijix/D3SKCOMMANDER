using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Toolbar;

/// <summary>What the shell knows when a button is pressed: the selection, the active folder and the user commands.</summary>
public sealed record ToolbarContext(
    IReadOnlyList<string> Files,
    string ActiveDirectory,
    IReadOnlyList<UserCommand> UserCommands);

/// <summary>What a button press runs: a catalog command, or processes to start. Both empty means nothing.</summary>
public sealed record ToolbarAction(CommandId? Command, IReadOnlyList<ProcessRequest> Processes)
{
    public static ToolbarAction None { get; } = new(null, []);

    public bool IsNothing => Command is null && Processes.Count == 0;
}

/// <summary>Turns a button into what to run. Pure: nothing starts here.</summary>
public static class ToolbarActions
{
    public static ToolbarAction Resolve(ToolbarItem item, ToolbarContext context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);
        return item.Kind switch
        {
            ToolbarItemKind.Command when item.Command is CommandId command => new ToolbarAction(command, []),
            ToolbarItemKind.UserCommand => ResolveUserCommand(item, context),
            ToolbarItemKind.Program => ResolveProgram(item, context),
            _ => ToolbarAction.None,
        };
    }

    // A Tools-menu command runs on the selected files, as from the menu.
    private static ToolbarAction ResolveUserCommand(ToolbarItem item, ToolbarContext context)
    {
        var command = context.UserCommands.FirstOrDefault(
            candidate => string.Equals(candidate.Name, item.UserCommandName, StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            return ToolbarAction.None;
        }

        var requests = CommandBuilder.Build(command.Command, context.Files, command.Mode)
            .Select(built => Start(built, Folder(context.ActiveDirectory)))
            .ToList();
        return new ToolbarAction(null, requests);
    }

    // The arguments use the user commands' rules (CommandBuilder): quotes group a word, %s is the
    // selected files. The program is its own field, so a path with spaces needs no quotes.
    private static ToolbarAction ResolveProgram(ToolbarItem item, ToolbarContext context)
    {
        if (string.IsNullOrWhiteSpace(item.Program))
        {
            return ToolbarAction.None;
        }

        var built = CommandBuilder.BuildAnySelection("program " + item.Arguments, context.Files);
        var arguments = built.Count == 0 ? [] : built[0].Arguments;
        var folder = Folder(string.IsNullOrWhiteSpace(item.WorkingFolder) ? context.ActiveDirectory : item.WorkingFolder.Trim());
        var request = new ProcessRequest.Argv(item.Program.Trim(), arguments, folder, false, false);
        return new ToolbarAction(null, [request]);
    }

    // The Computer address is not a folder a process can start in: it maps to Home, like the command line.
    private static string Folder(string? directory)
    {
        if (ComputerAddress.IsAddress(directory))
        {
            return LocalFileSystem.HomeDirectory();
        }

        return string.IsNullOrEmpty(directory) ? Environment.CurrentDirectory : directory;
    }

    private static ProcessRequest Start(LaunchCommand built, string directory) =>
        new ProcessRequest.Argv(built.Program, built.Arguments, directory, false, false);
}
