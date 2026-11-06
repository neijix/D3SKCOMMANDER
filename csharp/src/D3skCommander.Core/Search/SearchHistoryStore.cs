using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Search;

/// <summary>The search history file <see cref="SettingsFileNames.SearchHistory"/> in the settings folder.</summary>
public sealed class SearchHistoryStore : SettingsStore<SearchHistory>
{
    public SearchHistoryStore(string folder, TimeProvider? time = null)
        : base(folder, SettingsFileNames.SearchHistory, 1, time)
    {
    }
}

/// <summary>The saved search forms, file <see cref="SettingsFileNames.SearchTemplates"/> in the settings folder.</summary>
public sealed class SearchTemplateStore : SettingsStore<SearchTemplates>
{
    public SearchTemplateStore(string folder, TimeProvider? time = null)
        : base(folder, SettingsFileNames.SearchTemplates, 1, time)
    {
    }
}
