using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Toolbar;

/// <summary>What a toolbar button runs, or a rule between groups of buttons.</summary>
public enum ToolbarItemKind
{
    Command,
    UserCommand,
    Program,
    Separator,
}

/// <summary>
/// One entry of the main toolbar. Only the fields of its <see cref="Kind"/> count: the catalog
/// <see cref="Command"/>, the <see cref="UserCommandName"/> of a Tools-menu command, or a
/// <see cref="Program"/> with <see cref="Arguments"/> and a <see cref="WorkingFolder"/>.
/// <see cref="Icon"/> is a name from <see cref="ToolbarIcons"/> (empty: the default of the command);
/// <see cref="Tooltip"/> empty means the command's label and its key.
/// </summary>
public sealed record ToolbarItem(
    ToolbarItemKind Kind,
    CommandId? Command = null,
    string UserCommandName = "",
    string Program = "",
    string Arguments = "",
    string WorkingFolder = "",
    string Icon = "",
    string Tooltip = "")
{
    public static ToolbarItem Separator { get; } = new(ToolbarItemKind.Separator);

    public static ToolbarItem ForCommand(CommandId command, string icon = "", string tooltip = "") =>
        new(ToolbarItemKind.Command, command, Icon: icon, Tooltip: tooltip);

    public static ToolbarItem ForUserCommand(string name, string icon = "", string tooltip = "") =>
        new(ToolbarItemKind.UserCommand, UserCommandName: name, Icon: icon, Tooltip: tooltip);

    public static ToolbarItem ForProgram(string program, string arguments = "", string workingFolder = "", string icon = "", string tooltip = "") =>
        new(ToolbarItemKind.Program, Program: program, Arguments: arguments, WorkingFolder: workingFolder, Icon: icon, Tooltip: tooltip);

    /// <summary>The icon name to draw: the chosen one, else the kind's default.</summary>
    public string EffectiveIcon()
    {
        if (ToolbarIcons.IsKnown(Icon))
        {
            return Icon;
        }

        return Kind switch
        {
            ToolbarItemKind.Command when Command is CommandId command => ToolbarIcons.ForCommand(command),
            ToolbarItemKind.UserCommand => ToolbarIcons.UserCommand,
            ToolbarItemKind.Program => ToolbarIcons.Program,
            _ => ToolbarIcons.Generic,
        };
    }
}
