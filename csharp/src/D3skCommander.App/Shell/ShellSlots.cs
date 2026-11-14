namespace D3skCommander.App.Shell;

public enum PanelSide
{
    Left,
    Right,
}

/// <summary>Where a feature may add a view without editing the shell. Panels themselves are <see cref="PanelSide"/>.</summary>
public enum ShellRegion
{
    /// <summary>Between the menu bar and the panels.</summary>
    Toolbar,
    UnderPanels,
    BesidePanels,

    /// <summary>A narrow column between the left and the right panel.</summary>
    BetweenPanels,
}
