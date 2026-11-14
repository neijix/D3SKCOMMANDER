using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>One drawn entry of the toolbar or the middle bar: a rule, or a button with its icon, tooltip and state.</summary>
public sealed record ToolbarButton(ToolbarItem Item, string Icon, string Tooltip, bool IsEnabled)
{
    public bool IsSeparator => Item.Kind == ToolbarItemKind.Separator;

    /// <summary>The button of an item; a command nobody can run now is disabled.</summary>
    public static ToolbarButton Describe(ToolbarItem item, ILocalizer text, IShellContributions? shell)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(text);
        var enabled = item.Kind != ToolbarItemKind.Command
            || item.Command is not CommandId command
            || shell is null
            || shell.CanExecute(command);
        return new ToolbarButton(item, item.EffectiveIcon(), TooltipOf(item, text), enabled);
    }

    /// <summary>The user's text, else the command's label and its key, else the name of what the button starts.</summary>
    private static string TooltipOf(ToolbarItem item, ILocalizer text)
    {
        if (item.Tooltip.Length > 0)
        {
            return item.Tooltip;
        }

        if (item.Kind == ToolbarItemKind.Command && item.Command is CommandId command)
        {
            var label = text.Get(CommandCatalog.LabelKey(command));
            return CommandCatalog.PrimaryGesture(command) is Gesture gesture ? label + " (" + gesture.Format() + ")" : label;
        }

        if (item.Kind == ToolbarItemKind.UserCommand)
        {
            return item.UserCommandName;
        }

        return item.Kind == ToolbarItemKind.Program ? Path.GetFileName(item.Program.TrimEnd('/', '\\')) : "";
    }
}
