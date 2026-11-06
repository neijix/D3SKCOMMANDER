using D3skCommander.Core.Common;
using D3skCommander.Core.Search;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.App.Startup;

/// <summary>
/// The settings folder this run uses and its stores (preferences, session, hotlist, search history and templates).
/// Every feature reads and saves its settings here; the stores debounce their own writes.
/// </summary>
public sealed class AppSettings : IDisposable
{
    public AppSettings(SettingsFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        Folder = folder;
        TryCreate(folder.Dir);
        Preferences = new PreferencesStore(folder.Dir);
        Session = new SessionStore(folder.Dir);
        Hotlist = new HotlistStore(folder.Dir);
        SearchHistory = new SearchHistoryStore(folder.Dir);
        SearchTemplates = new SearchTemplateStore(folder.Dir);
    }

    public SettingsFolder Folder { get; }

    public string Directory => Folder.Dir;

    public PreferencesStore Preferences { get; }

    public SessionStore Session { get; }

    public HotlistStore Hotlist { get; }

    public SearchHistoryStore SearchHistory { get; }

    public SearchTemplateStore SearchTemplates { get; }

    /// <summary>The settings folder of this process (<see cref="SettingsDirectory.Resolve"/> on the current places).</summary>
    public static AppSettings ForThisProcess() => new(SettingsDirectory.Resolve(SettingsPlaces.Current()));

    /// <summary>Load errors of the stores, each reported once.</summary>
    public IReadOnlyList<OperationError> TakeLoadErrors()
    {
        var errors = new List<OperationError>();
        Add(errors, Preferences.TakeLoadError());
        Add(errors, Session.TakeLoadError());
        Add(errors, Hotlist.TakeLoadError());
        Add(errors, SearchHistory.TakeLoadError());
        Add(errors, SearchTemplates.TakeLoadError());
        return errors;
    }

    /// <summary>Writes any debounced save now.</summary>
    public void Flush()
    {
        Preferences.Flush();
        Session.Flush();
        Hotlist.Flush();
        SearchHistory.Flush();
        SearchTemplates.Flush();
    }

    public void Dispose()
    {
        Preferences.Dispose();
        Session.Dispose();
        Hotlist.Dispose();
        SearchHistory.Dispose();
        SearchTemplates.Dispose();
    }

    private static void Add(List<OperationError> errors, OperationError? error)
    {
        if (error is not null)
        {
            errors.Add(error);
        }
    }

    private static void TryCreate(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
        }
        catch (IOException)
        {
            // The stores report a folder they cannot use as a load or save error.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
