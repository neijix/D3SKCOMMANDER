namespace D3skCommander.App.Shell;

public enum PanelSide
{
    Left,
    Right,
}

/// <summary>Where a feature may add a view without editing the shell. Panels themselves are <see cref="PanelSide"/>.</summary>
public enum ShellRegion
{
    UnderPanels,
    BesidePanels,
}
