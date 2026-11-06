using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    private Func<CommandId, bool>? _dispatch;

    /// <summary>The icon buttons in front of the path box: drives, parent, hotlist, history, network, FTP.</summary>
    public IReadOnlyList<PanelBarButton> BarButtons { get; private set; } = [];

    /// <summary>A bar button was clicked: this side becomes active, then the shell runs the command.</summary>
    public void RunBar(CommandId command)
    {
        Activate();
        _dispatch?.Invoke(command);
    }

    private void InitBar(Func<CommandId, bool>? dispatch)
    {
        _dispatch = dispatch;
        var buttons = new List<PanelBarButton>();
        foreach (var command in CommandCatalog.PanelBar(_side == PanelSide.Left))
        {
            var label = _text.Get(CommandCatalog.LabelKey(command));
            var tip = CommandCatalog.PrimaryGesture(command) is { } gesture ? $"{label} ({gesture.Format()})" : label;
            buttons.Add(new PanelBarButton(command, label, tip));
        }

        BarButtons = buttons;
    }
}
