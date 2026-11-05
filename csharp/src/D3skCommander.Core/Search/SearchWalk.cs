using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Search;

/// <summary>The state of one running search: the counters and the folders still to visit.</summary>
internal sealed class SearchWalk
{
    private readonly SearchEngine _engine;
    private readonly SearchQuery _query;
    private readonly SearchMatchers _matchers;
    private long _seen;

    public SearchWalk(SearchEngine engine, SearchQuery query, SearchMatchers matchers)
    {
        _engine = engine;
        _query = query;
        _matchers = matchers;
    }

    public long Hits { get; private set; }

    public async IAsyncEnumerable<SearchEvent> RootAsync(
        string root,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var inArchive = _query.SearchArchives && ArchiveFileSystemProvider.IsArchivePath(root);
        var first = await _engine.Provider(inArchive).GetEntryAsync(root, cancellationToken).ConfigureAwait(false);
        if (!first.IsSuccess || !first.Value.IsDirectory)
        {
            var error = first.Error ?? OperationError.InvalidData("not a folder", root);
            yield return SearchEvent.ForError(error);
            yield break;
        }

        var pending = new Stack<Frame>();
        pending.Push(new Frame(root, 1, inArchive));
        while (pending.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var frame = pending.Pop();
            var provider = _engine.Provider(frame.InArchive);
            await foreach (var item in provider.ListAsync(frame.Path, _query.IncludeHidden, cancellationToken)
                .ConfigureAwait(false))
            {
                if (!item.IsSuccess)
                {
                    if (item.Error is not null && item.Error.Kind != ErrorKind.Cancelled)
                    {
                        yield return SearchEvent.ForError(item.Error);
                    }

                    continue;
                }

                var entry = item.Value;
                if (entry.IsParent)
                {
                    continue;
                }

                _seen++;
                if (_seen % SearchEngine.ScanEvery == 0)
                {
                    yield return SearchEvent.ForScanned(_seen, entry.Path);
                }

                var evaluated = await EvaluateAsync(provider, entry, cancellationToken).ConfigureAwait(false);
                if (evaluated.Hit is not null)
                {
                    Hits++;
                    yield return SearchEvent.ForHit(evaluated.Hit);
                }

                if (evaluated.Failure is not null)
                {
                    yield return SearchEvent.ForError(evaluated.Failure);
                }

                Descend(pending, frame, entry);
            }
        }
    }

    private void Descend(Stack<Frame> pending, Frame frame, Entry entry)
    {
        if (_query.MaxDepth is int limit && frame.Depth >= limit)
        {
            return;
        }

        if (entry.IsDirectory && !entry.IsLink)
        {
            pending.Push(new Frame(entry.Path, frame.Depth + 1, frame.InArchive));
        }
        else if (!entry.IsDirectory && !frame.InArchive && _query.SearchArchives
            && ArchiveFileSystemProvider.IsArchiveFile(entry.Path))
        {
            pending.Push(new Frame(entry.Path, frame.Depth + 1, true));
        }
    }

    private async Task<Evaluated> EvaluateAsync(
        IFileSystemProvider provider,
        Entry entry,
        CancellationToken cancellationToken)
    {
        if ((entry.IsDirectory && !_query.Dirs) || (!entry.IsDirectory && !_query.Files))
        {
            return Evaluated.None;
        }

        if (!_matchers.NameMatches(entry.Name) || !PassesMeta(entry))
        {
            return Evaluated.None;
        }

        if (_matchers.Content is null)
        {
            return Evaluated.Found(Hit(entry, null));
        }

        if (entry.IsDirectory)
        {
            return Evaluated.None;
        }

        var scan = await ContentScanner.ScanAsync(provider, entry.Path, entry.Size, _matchers, cancellationToken)
            .ConfigureAwait(false);
        if (scan.Failure is not null)
        {
            return Evaluated.Failed(OperationError.FromMessage(scan.Failure, entry.Path));
        }

        if (scan.Kind == ScanKind.Found && !_query.NotContaining)
        {
            return Evaluated.Found(Hit(entry, scan));
        }

        if (scan.Kind == ScanKind.NotFound && _query.NotContaining)
        {
            return Evaluated.Found(Hit(entry, null));
        }

        return Evaluated.None;
    }

    private bool PassesMeta(Entry entry)
    {
        if (!entry.IsDirectory
            && ((_query.MinSize is ulong min && entry.Size < min) || (_query.MaxSize is ulong max && entry.Size > max)))
        {
            return false;
        }

        if (_query.NewerThan is null && _query.OlderThan is null)
        {
            return true;
        }

        if (entry.Modified is not DateTimeOffset modified)
        {
            return false;
        }

        return !(_query.NewerThan is DateTimeOffset newer && modified <= newer)
            && !(_query.OlderThan is DateTimeOffset older && modified >= older);
    }

    private static SearchHit Hit(Entry entry, ScanResult? scan)
    {
        var size = entry.IsDirectory ? 0 : entry.Size;
        if (scan is ScanResult found)
        {
            return new SearchHit(entry.Path, entry.Name, entry.IsDirectory, size, entry.Modified, found.Line, found.Text);
        }

        return new SearchHit(entry.Path, entry.Name, entry.IsDirectory, size, entry.Modified, null, null);
    }

    private readonly record struct Frame(string Path, int Depth, bool InArchive);

    private readonly record struct Evaluated(SearchHit? Hit, OperationError? Failure)
    {
        public static Evaluated None => new(null, null);

        public static Evaluated Found(SearchHit hit) => new(hit, null);

        public static Evaluated Failed(OperationError error) => new(null, error);
    }
}
