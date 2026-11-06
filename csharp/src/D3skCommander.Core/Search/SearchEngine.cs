using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
        var files = new List<DuplicateFile>();
        var duplicates = query.Duplicates;
        foreach (var root in query.Roots)
        {
            await foreach (var step in walk.RootAsync(root, cancellationToken).ConfigureAwait(false))
            {
                if (duplicates is not null && step.Kind == SearchEventKind.Hit && step.Hit is not null)
                {
                    files.Add(new DuplicateFile(step.Hit.Path, step.Hit.Size));
                    continue;
                }

                yield return step;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        if (duplicates is null)
        {
            yield return SearchEvent.ForDone(walk.Hits, cancellationToken.IsCancellationRequested);
            yield break;
        }

        var outcome = new DuplicateOutcome();
        if (!cancellationToken.IsCancellationRequested)
        {
            await foreach (var step in GroupAsync(files, duplicates, outcome, cancellationToken).ConfigureAwait(false))
            {
                yield return step;
            }
        }

        yield return SearchEvent.ForDone(outcome.Hits, cancellationToken.IsCancellationRequested, outcome.Skipped);
    }

    private sealed class DuplicateOutcome
    {
        public long Hits { get; set; }

        public int Skipped { get; set; }
    }

    // Hashing runs on its own task and reports through a channel, so progress can be yielded as it comes.
    private static async IAsyncEnumerable<SearchEvent> GroupAsync(
        List<DuplicateFile> files,
        DuplicateCriteria criteria,
        DuplicateOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SearchEvent>();
        var work = Task.Run(
            async () =>
            {
                try
                {
                    return await new DuplicateFinder()
                        .FindAsync(files, criteria, (done, total) => channel.Writer.TryWrite(SearchEvent.ForHashing(done, total)), cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            },
            CancellationToken.None);
        await foreach (var step in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            yield return step;
        }

        DuplicateResult? found = null;
        try
        {
            found = await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (found is null)
        {
            yield break;
        }

        outcome.Skipped = found.Skipped;
        foreach (var group in found.Groups)
        {
            foreach (var file in group.Files)
            {
                outcome.Hits++;
                yield return SearchEvent.ForHit(HitOf(file, group.Number));
            }
        }
    }

    private static SearchHit HitOf(DuplicateFile file, int group)
    {
        var name = Path.GetFileName(file.Path);
        DateTimeOffset? modified = null;
        try
        {
            modified = new DateTimeOffset(File.GetLastWriteTimeUtc(file.Path), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return new SearchHit(file.Path, name, false, file.Size, modified, null, null, group);
    }

    internal IFileSystemProvider Provider(bool inArchive) => inArchive ? _archives : _local;
}
