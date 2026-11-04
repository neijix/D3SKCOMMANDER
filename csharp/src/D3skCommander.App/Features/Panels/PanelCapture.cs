namespace D3skCommander.App.Features.Panels;

/// <summary>Paths taken at the moment a command starts, so a dialog cannot race the panels.</summary>
public sealed class PanelCapture
{
    public PanelCapture(IReadOnlyList<string> sources, string activeDirectory, string otherDirectory)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrEmpty(activeDirectory);
        ArgumentException.ThrowIfNullOrEmpty(otherDirectory);
        Sources = sources;
        ActiveDirectory = activeDirectory;
        OtherDirectory = otherDirectory;
    }

    public IReadOnlyList<string> Sources { get; }

    public string ActiveDirectory { get; }

    public string OtherDirectory { get; }
}
