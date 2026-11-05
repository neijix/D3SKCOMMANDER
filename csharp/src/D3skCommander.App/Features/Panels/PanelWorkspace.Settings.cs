using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    /// <summary>The detail columns the preferences show (every column when there is no settings store).</summary>
    public Columns Columns => _settings?.Preferences.Current.Columns ?? Columns.Default;

    /// <summary>The visited folders, latest first, as the session keeps them.</summary>
    public IReadOnlyList<string> History => _settings is null ? [] : [.. _settings.Session.Current.History];

    private void SaveShowHidden()
    {
        if (_settings is null)
        {
            return;
        }

        var preferences = _settings.Preferences.Current;
        preferences.ShowHidden = _showHidden;
        _settings.Preferences.Save(preferences);
    }

    private void RememberVisit(string path)
    {
        if (_settings is null || string.IsNullOrEmpty(path))
        {
            return;
        }

        var session = _settings.Session.Current;
        session.RememberDirectory(path);
        _settings.Session.Save(session);
    }
}
