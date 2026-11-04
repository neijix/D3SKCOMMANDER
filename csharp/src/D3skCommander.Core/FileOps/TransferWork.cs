using System.Diagnostics.CodeAnalysis;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>One job's copy, move, or delete. The job service calls <see cref="RunAsync"/> per source.</summary>
internal sealed partial class TransferWork
{
    private readonly FileOperationService _service;
    private readonly IOperationLog _log;
    private readonly FileOpKind _kind;
    private readonly string? _directory;
    private readonly string? _rename;
    private readonly OperationError? _planError;
    private readonly ConflictPolicy _conflicts;
    private readonly Action<FileOpProgress>? _onProgress;
    private readonly CancellationToken _external;
    private IJob? _job;

    public TransferWork(
        FileOperationService service,
        IOperationLog log,
        FileOpRequest request,
        string? directory,
        string? rename,
        OperationError? planError,
        FileOpSession session,
        ConflictPolicy conflicts,
        Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>>? ask,
        Action<FileOpProgress>? onProgress,
        CancellationToken external)
    {
        _service = service;
        _log = log;
        Source = request.Source;
        Destination = request.Destination ?? request.Source;
        _kind = request.Kind;
        _directory = directory;
        _rename = rename;
        _planError = planError;
        Session = session;
        _conflicts = conflicts;
        Ask = ask;
        _onProgress = onProgress;
        _external = external;
    }

    public IFileSystemProvider Source { get; }

    public IFileSystemProvider Destination { get; }

    public FileOpSession Session { get; }

    public bool Moving => _kind == FileOpKind.Move;

    internal Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>>? Ask { get; }

    public string OperationName
    {
        get
        {
            if (_kind == FileOpKind.Move)
            {
                return "MOVE";
            }

            if (_kind == FileOpKind.Delete)
            {
                return "DELETE";
            }

            return "COPY";
        }
    }

    public async Task<ItemResult> RunAsync(JobItem item, IJobProgress progress, CancellationToken cancellationToken)
    {
        _job = _service.FindRunning(item.Name);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _external);
        if (progress.Current.ItemsDone == 0)
        {
            Session.Reset();
            _conflicts.Reset();
        }

        if (linked.IsCancellationRequested)
        {
            Abort();
        }

        if (_planError is not null)
        {
            Fail(item.Name, null, _planError, progress, 0, 0, false);
            return ItemResult.Failed(_planError);
        }

        if (_kind == FileOpKind.Delete)
        {
            return await FileDelete.RunAsync(this, item.Name, progress, linked.Token).ConfigureAwait(false);
        }

        return await TransferSourceAsync(item.Name, progress, linked.Token).ConfigureAwait(false);
    }

    [DoesNotReturn]
    public void Abort()
    {
        Session.MarkCancelled();
        if (_job is not null)
        {
            _job.Cancel();
        }

        throw new OperationCanceledException();
    }

    public void Report(IJobProgress progress, bool force) => Session.Report(progress, _onProgress, force);

    public void Succeed(string source, string? destination, int files, long bytes, IJobProgress progress)
    {
        Session.Complete(files, bytes);
        FileOpLog.Write(_log, OperationName, source, destination, null);
        Report(progress, true);
    }

    public void Fail(
        string source,
        string? destination,
        OperationError error,
        IJobProgress progress,
        int files,
        long bytes,
        bool count)
    {
        Session.AddError(error);
        if (count)
        {
            Session.Skip(files, bytes);
        }

        FileOpLog.Write(_log, OperationName, source, destination, error.Message);
        Report(progress, true);
    }

    private async Task<ItemResult> TransferSourceAsync(string source, IJobProgress progress, CancellationToken token)
    {
        if (!await EnsureDestinationAsync(progress, token).ConfigureAwait(false))
        {
            return ItemResult.Failed(Session.LastError ?? OperationError.Other("no destination folder", source));
        }

        var count = await TreeCount.CountAsync(Source, source, token).ConfigureAwait(false);
        Session.AddTotals(count.Files, count.Bytes);
        var leaf = _rename ?? FileOpNames.Leaf(source);
        if (leaf is null)
        {
            var error = OperationError.Other("the path has no file name", source);
            Fail(source, null, error, progress, count.Files, count.Bytes, true);
            return ItemResult.Failed(error);
        }

        var before = Session.ErrorCount;
        var target = FileOpNames.Join(_directory ?? "", leaf);
        await FileMove.TransferAsync(this, source, target, count, progress, token).ConfigureAwait(false);
        Report(progress, true);
        if (Session.ErrorCount > before)
        {
            return ItemResult.Failed(Session.LastError ?? OperationError.Other("the item failed", source));
        }

        return ItemResult.Success;
    }

    private async Task<bool> EnsureDestinationAsync(IJobProgress progress, CancellationToken token)
    {
        if (Session.DestinationError is not null)
        {
            return false;
        }

        if (Session.DestinationReady)
        {
            return true;
        }

        var directory = _directory ?? "";
        var error = await FolderChain.EnsureAsync(Destination, directory, token).ConfigureAwait(false);
        Session.DestinationReady = true;
        if (error is null)
        {
            return true;
        }

        if (error.Kind == ErrorKind.Cancelled)
        {
            Abort();
        }

        Session.DestinationError = error;
        Fail(directory, null, error, progress, 0, 0, false);
        return false;
    }
}
