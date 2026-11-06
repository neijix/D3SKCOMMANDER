using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Panels;

/// <summary>The favorite tabs file <see cref="SettingsFileNames.FavoriteTabs"/> in the settings folder; an unreadable file gives an empty list.</summary>
public sealed class FavoriteTabsStore : SettingsStore<FavoriteTabs>
{
    public FavoriteTabsStore(string folder, TimeProvider? time = null)
        : base(folder, SettingsFileNames.FavoriteTabs, 1, time)
    {
    }
}
