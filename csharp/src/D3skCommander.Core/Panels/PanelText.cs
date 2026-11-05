using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Panels;

/// <summary>The text copied for a selection: one name or full path per line.</summary>
public static class PanelText
{
    public static string Names(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return Lines(entries.Select(entry => entry.Name));
    }

    public static string Paths(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return Lines(entries.Select(entry => entry.Path));
    }

    private static string Lines(IEnumerable<string> lines) => string.Join('\n', lines);
}
