using D3skCommander.Core.Commands;

namespace D3skCommander.App.Features.Panels;

/// <summary>One icon button in front of the path box: its command and its tooltip (label and key).</summary>
public sealed record PanelBarButton(CommandId Command, string Label, string Tip);
