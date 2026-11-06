using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>One drawn entry of the toolbar: a rule, or a button with its icon, tooltip and state.</summary>
public sealed record ToolbarButton(ToolbarItem Item, string Icon, string Tooltip, bool IsEnabled)
{
    public bool IsSeparator => Item.Kind == ToolbarItemKind.Separator;
}
