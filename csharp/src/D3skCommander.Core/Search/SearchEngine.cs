using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Search;

/// <summary>
/// Walks the roots of a <see cref="SearchQuery"/> and streams what matches. Folders come from the
/// local provider; an archive file is walked through the archive provider when the query asks for it.
/// A failure to read one folder is an Error event and the walk goes on. The stream always ends with
/// a Done event, whether the search finished, was cancelled, or had an invalid pattern.
/// </summary>
public sealed class SearchEngine
{
    /// <summary>A Scanned event is sent every this many entries.</summary>
    public const int ScanEvery = 64;

    private readonly IFileSystemProvider _local;
    private readonly IFileSystemProvider _archives;

    public SearchEngine()
        : this(new LocalFileSystemProvider(), new ArchiveFileSystemProvider())
    {
    }

    public SearchEngine(IFileSystemProvider local, IFileSystemProvider archives)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(archives);
        _local = local;
        _archives = archives;
    }

    public async IAsyncEnumerable<SearchEvent> RunAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var built = SearchMatchers.Build(query);
        if (built.Matchers is null)
        {
            yield return SearchEvent.ForError(built.Error ?? OperationError.InvalidData("invalid pattern"));
            yield return SearchEvent.ForDone(0, false);
            yield break;
        }

        var walk = new SearchWalk(this, query, built.Matchers);
        foreach (var root in query.Roots)
        {
            await foreach (var step in walk.RootAsync(root, cancellationToken).ConfigureAwait(false))
            {
                yield return step;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        yield return SearchEvent.ForDone(walk.Hits, cancellationToken.IsCancellationRequested);
    }

    internal IFileSystemProvider Provider(bool inArchive) => inArchive ? _archives : _local;
}
