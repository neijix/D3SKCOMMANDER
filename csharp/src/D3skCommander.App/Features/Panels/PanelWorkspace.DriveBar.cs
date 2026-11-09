using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    private bool _showDriveBar = true;

    /// <summary>Whether the drive bar shows above both panels (saved in the preferences).</summary>
    public bool ShowDriveBar => _showDriveBar;

    /// <summary>The favorite folders (the hotlist), shown after the drives in the drive bar; empty without settings.</summary>
    public IReadOnlyList<Core.Settings.HotlistItem> Favorites => _settings?.Hotlist.Current.Items ?? [];

    public void ToggleDriveBar()
    {
        _showDriveBar = !_showDriveBar;
        if (_settings is null)
        {
            return;
        }

        var preferences = _settings.Preferences.Current;
        preferences.ShowDriveBar = _showDriveBar;
        _settings.Preferences.Save(preferences);
    }

    /// <summary>Opens <paramref name="path"/> in a new tab of <paramref name="side"/>.</summary>
    public async Task NewTabAtAsync(PanelSide side, string path, CancellationToken cancellationToken = default)
    {
        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        Activate(side);
        await NewTabAsync(cancellationToken).ConfigureAwait(false);
        await GoAsync(side, path, cancellationToken).ConfigureAwait(false);
    }
}
