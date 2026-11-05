using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.QuickView;

/// <summary>The short text the quick view shows for a folder: its name and how many items it holds.</summary>
internal static class QuickViewSummary
{
    private const int Cap = 10000;

    /// <summary>Items directly inside the folder (stops at the cap), or -1 when it cannot be read.</summary>
    public static int Count(string folder)
    {
        try
        {
            var count = 0;
            foreach (var _ in Directory.EnumerateFileSystemEntries(folder))
            {
                if (++count >= Cap)
                {
                    break;
                }
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return -1;
        }
    }

    public static string Describe(ILocalizer text, string folder, int count)
    {
        ArgumentNullException.ThrowIfNull(text);
        var name = Path.GetFileName(folder);
        if (count < 0)
        {
            return name + "\n" + text.Get("quickview.unreadable");
        }

        var more = count >= Cap ? "+" : "";
        return name + "\n" + text.Get("quickview.items").Replace("{count}", count + more, StringComparison.Ordinal);
    }
}
