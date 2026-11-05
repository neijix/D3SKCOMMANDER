using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Archives;

/// <summary>Pack, extract and backup running on the job service, with progress and cancel.</summary>
public sealed class ArchiveService
{
    private readonly IJobService _jobs;

    public ArchiveService(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        _jobs = jobs;
    }

    public ArchiveJob Pack(
        string archive,
        ArchiveKind kind,
        IReadOnlyList<string> sources,
        int level,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentNullException.ThrowIfNull(sources);
        return Start("Pack", archive, cancellationToken, (progress, token, session) =>
            RunCreate(session, archive, kind, sources, level, progress, token));
    }

    public ArchiveJob Extract(
        string archive,
        IReadOnlyList<string>? entries,
        string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        return Start("Extract", archive, cancellationToken, (progress, token, session) =>
            RunExtract(session, archive, entries, destination, progress, token));
    }

    /// <summary>The selection, zipped into <paramref name="destinationDirectory"/> at level 6.</summary>
    public ArchiveJob Backup(
        string destinationDirectory,
        IReadOnlyList<string> sources,
        string? activeDirectory,
        DateTimeOffset localStamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        var name = BackupZip.FileName(BackupZip.BaseName(sources, activeDirectory), localStamp);
        return Pack(Path.Combine(destinationDirectory, name), ArchiveKind.Zip, sources, 6, cancellationToken);
    }

    private ArchiveJob Start(
        string title,
        string itemName,
        CancellationToken external,
        Func<IJobProgress, CancellationToken, ArchiveSession, ItemResult> work)
    {
        var session = new ArchiveSession();
        var ready = new TaskCompletionSource<IJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = _jobs.Start(title, [new JobItem(itemName)], async (_, progress, token) =>
        {
            var self = await ready.Task.WaitAsync(token).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, external);
            if (linked.IsCancellationRequested)
            {
                self.Cancel();
                throw new OperationCanceledException();
            }

            try
            {
                return work(progress, linked.Token, session);
            }
            catch (OperationCanceledException)
            {
                self.Cancel();
                throw;
            }
        });
        ready.SetResult(job);
        return new ArchiveJob(job, session);
    }

    private static ItemResult RunCreate(
        ArchiveSession session,
        string archive,
        ArchiveKind kind,
        IReadOnlyList<string> sources,
        int level,
        IJobProgress progress,
        CancellationToken token)
    {
        var result = ArchiveIo.Create(archive, kind, sources, level, name => Report(session, progress, name, token), token);
        return Finish(session, result, token);
    }

    private static ItemResult RunExtract(
        ArchiveSession session,
        string archive,
        IReadOnlyList<string>? entries,
        string destination,
        IJobProgress progress,
        CancellationToken token)
    {
        var result = ArchiveIo.Extract(archive, entries, destination, name => Report(session, progress, name, token), token);
        return Finish(session, result, token);
    }

    private static bool Report(ArchiveSession session, IJobProgress progress, string name, CancellationToken token)
    {
        session.Add(name);
        var current = progress.Current;
        progress.Report(new JobProgress(
            current.ItemsDone + 1,
            Math.Max(current.ItemsTotal, 1),
            current.BytesDone,
            current.BytesTotal,
            name));
        return !token.IsCancellationRequested;
    }

    private static ItemResult Finish(ArchiveSession session, ArchiveResult<int> result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (result.Error is not null)
        {
            if (result.Error.Kind == ErrorKind.Cancelled)
            {
                throw new OperationCanceledException(token);
            }

            return ItemResult.Failed(result.Error);
        }

        session.FileCount = result.Value;
        return ItemResult.Success;
    }
}

/// <summary>A pack, extract or backup queued on the job service.</summary>
public sealed class ArchiveJob
{
    private readonly ArchiveSession _session;

    internal ArchiveJob(IJob job, ArchiveSession session)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(session);
        Job = job;
        _session = session;
    }

    public IJob Job { get; }

    public int FileCount => _session.FileCount;

    public IReadOnlyList<string> Names => _session.Copy();
}

internal sealed class ArchiveSession
{
    private readonly object _gate = new();
    private readonly List<string> _names = [];

    public int FileCount { get; set; }

    public void Add(string name)
    {
        lock (_gate)
        {
            _names.Add(name);
        }
    }

    public string[] Copy()
    {
        lock (_gate)
        {
            return _names.ToArray();
        }
    }
}
