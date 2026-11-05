using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Catalog;

/// <summary>
/// Disk catalog in the <c>.d3cat</c> JSON Lines format. Creating one runs on the job service.
/// Search reads the catalog file and does not need the original folder.
/// </summary>
public static partial class DiskCatalog
{
    public const int SchemaVersion = 1;

    public const string FileExtension = ".d3cat";

    /// <summary>Queues a catalog of <paramref name="root"/> written to <paramref name="output"/>. Cancel removes the file.</summary>
    public static IJob Start(IJobService jobs, string title, string root, string output)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(output);
        return jobs.Start(title, [new JobItem(output)], (_, progress, token) => Run(root, output, progress, token));
    }

    /// <summary>Entries whose path contains <paramref name="query"/>, case-insensitive. A blank query returns every entry.</summary>
    public static IReadOnlyList<CatalogEntry> Search(CatalogDocument catalog, string query)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        var needle = query.Trim();
        if (needle.Length == 0)
        {
            return catalog.Entries;
        }

        var found = new List<CatalogEntry>();
        foreach (var entry in catalog.Entries)
        {
            if (entry.Path.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(entry);
            }
        }

        return found;
    }

    private static Task<ItemResult> Run(string root, string output, IJobProgress progress, CancellationToken token)
    {
        var written = Write(root, output, token, progress);
        var error = written.Error;
        if (error is null)
        {
            return Task.FromResult(ItemResult.Success);
        }

        if (error.Kind == ErrorKind.Cancelled)
        {
            throw new OperationCanceledException(error.Message);
        }

        return Task.FromResult(ItemResult.Failed(error, retryable: true));
    }
}
