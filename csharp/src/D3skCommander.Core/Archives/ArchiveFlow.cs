using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

internal static class ArchiveFlow
{
    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List<T>(
        string path,
        Func<string, T> open,
        Func<T, IEnumerable<MemberView>> views)
        where T : IDisposable
    {
        try
        {
            using var archive = open(path);
            var entries = new List<ArchiveEntry>();
            foreach (var view in views(archive))
            {
                ArchiveExtract.AddListed(entries, view);
            }

            return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Ok(ArchivePaths.WithImpliedDirectories(entries));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Fail(ArchiveErrors.From(ex, path));
        }
    }

    public static ArchiveResult<int> Extract<T>(
        string path,
        Func<string, T> open,
        Func<T, IEnumerable<MemberView>> views,
        ArchivePlan plan,
        string destination,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
        where T : IDisposable
    {
        try
        {
            using var archive = open(path);
            return ArchiveExtract.Read(views(archive), plan, destination, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ArchiveResult<int>.Fail(ArchiveErrors.From(ex, path));
        }
    }

    public static ArchiveResult<int> Write(
        IReadOnlyList<SourceItem> items,
        Func<string, bool>? progress,
        CancellationToken cancellationToken,
        Action<SourceItem> write)
    {
        var count = 0;
        var cancelled = false;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (progress is not null && !progress(item.Name))
            {
                cancelled = true;
                break;
            }

            write(item);
            if (!item.IsDirectory)
            {
                count++;
            }
        }

        if (!cancelled)
        {
            return ArchiveResult<int>.Ok(count);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return ArchiveResult<int>.Fail(OperationError.Cancelled());
    }
}
