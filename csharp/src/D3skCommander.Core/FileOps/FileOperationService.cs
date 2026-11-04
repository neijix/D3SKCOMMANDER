using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>
/// Copy, move, delete, and new folder between providers. Work runs on the job service.
/// An overwrite truncates the target in place. Cancelling that write removes the partial target.
/// </summary>
public sealed class FileOperationService
{
    private readonly IJobService _jobs;
    private readonly IOperationLog _log;

    public FileOperationService(IJobService jobs, IOperationLog log)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(log);
        _jobs = jobs;
        _log = log;
    }

    public FileOperation Copy(
        IFileSystemProvider source,
        IReadOnlyList<string> sources,
        IFileSystemProvider destination,
        string? destinationDirectory,
        string? renameTo = null,
        Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>>? resolveConflict = null,
        Action<FileOpProgress>? onProgress = null,
        CancellationToken cancellationToken = default) =>
        Start(
            new FileOpRequest(FileOpKind.Copy, source, sources, destination, destinationDirectory, renameTo),
            resolveConflict,
            onProgress,
            cancellationToken);

    public FileOperation Move(
        IFileSystemProvider source,
        IReadOnlyList<string> sources,
        IFileSystemProvider destination,
        string? destinationDirectory,
        string? renameTo = null,
        Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>>? resolveConflict = null,
        Action<FileOpProgress>? onProgress = null,
        CancellationToken cancellationToken = default) =>
        Start(
            new FileOpRequest(FileOpKind.Move, source, sources, destination, destinationDirectory, renameTo),
            resolveConflict,
            onProgress,
            cancellationToken);

    public FileOperation Delete(
        IFileSystemProvider source,
        IReadOnlyList<string> sources,
        CancellationToken cancellationToken = default) =>
        Start(new FileOpRequest(FileOpKind.Delete, source, sources), null, null, cancellationToken);

    /// <summary>Dialog target: a folder, or a new name when one source is sent at a path that is not a folder.</summary>
    public FileOperation TransferToTarget(
        FileOpKind kind,
        IFileSystemProvider source,
        IReadOnlyList<string> sources,
        IFileSystemProvider destination,
        string target,
        bool targetIsDirectory,
        Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>>? resolveConflict = null,
        Action<FileOpProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (kind == FileOpKind.Delete)
        {
            throw new ArgumentException("Delete has no target folder.", nameof(kind));
        }

        var resolved = FileOpNames.FromDialog(target, sources.Count, targetIsDirectory);
        return Start(
            new FileOpRequest(kind, source, sources, destination, resolved.Directory, resolved.RenameTo),
            resolveConflict,
            onProgress,
            cancellationToken);
    }

    /// <summary>Empty <paramref name="name"/> does nothing, matching v0.1. An existing folder is success.</summary>
    public FileOperation NewFolder(
        IFileSystemProvider provider,
        string parentDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrEmpty(parentDirectory);
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            return Empty("New folder");
        }

        var path = Path.Combine(parentDirectory, name);
        var session = new FileOpSession();
        var job = _jobs.Start(
            "New folder",
            [new JobItem(path)],
            (item, progress, token) => RunNewFolder(session, provider, item.Name, progress, token, cancellationToken));
        return new FileOperation(job, session);
    }

    public FileOperation Start(
        FileOpRequest request,
        Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>>? resolveConflict = null,
        Action<FileOpProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var planned = Plan(request);
        var session = new FileOpSession();
        var conflicts = new ConflictPolicy();
        var work = new TransferWork(
            this,
            _log,
            request,
            planned.Directory,
            planned.Rename,
            planned.Error,
            session,
            conflicts,
            resolveConflict,
            onProgress,
            cancellationToken);
        var job = _jobs.Start(planned.Title, planned.Items, work.RunAsync);
        return new FileOperation(job, session);
    }

    internal IJob FindRunning(string itemName)
    {
        IJob? named = null;
        IJob? only = null;
        var running = 0;
        foreach (var candidate in _jobs.Jobs)
        {
            if (!candidate.IsRunning)
            {
                continue;
            }

            running++;
            only = candidate;
            if (!string.Equals(candidate.Progress.CurrentName, itemName, StringComparison.Ordinal))
            {
                continue;
            }

            if (named is null || candidate.Id > named.Id)
            {
                named = candidate;
            }
        }

        if (named is not null)
        {
            return named;
        }

        if (running == 1 && only is not null)
        {
            return only;
        }

        throw new InvalidOperationException("The file operation is not running.");
    }

    private async Task<ItemResult> RunNewFolder(
        FileOpSession session,
        IFileSystemProvider provider,
        string path,
        IJobProgress progress,
        CancellationToken jobToken,
        CancellationToken external)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(jobToken, external);
        return await FileNewFolder.RunAsync(this, session, _log, provider, path, progress, linked.Token)
            .ConfigureAwait(false);
    }

    private FileOperation Empty(string title)
    {
        var session = new FileOpSession();
        var job = _jobs.Start(title, [], (_, _, _) => Task.FromResult(ItemResult.Success));
        return new FileOperation(job, session);
    }

    private static Planned Plan(FileOpRequest request)
    {
        var title = Title(request.Kind);
        if (request.Kind == FileOpKind.Delete)
        {
            return Planned.Ready(title, Items(request.Sources), null, null, null);
        }

        if (request.Destination is null || string.IsNullOrEmpty(request.DestinationDirectory))
        {
            return Planned.Invalid(title, request, OperationError.Other("no destination folder"));
        }

        var renameError = FileOpNames.ValidateRename(request.RenameTo, request.Sources.Count, out var rename);
        if (renameError is not null)
        {
            return Planned.Invalid(title, request, renameError);
        }

        return Planned.Ready(title, Items(request.Sources), FileOpNames.Full(request.DestinationDirectory), rename, null);
    }

    private static string Title(FileOpKind kind)
    {
        if (kind == FileOpKind.Move)
        {
            return "Move";
        }

        if (kind == FileOpKind.Delete)
        {
            return "Delete";
        }

        return "Copy";
    }

    private static JobItem[] Items(IReadOnlyList<string> sources)
    {
        var items = new JobItem[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            items[i] = new JobItem(sources[i]);
        }

        return items;
    }

    private readonly struct Planned
    {
        private Planned(string title, JobItem[] items, string? directory, string? rename, OperationError? error)
        {
            Title = title;
            Items = items;
            Directory = directory;
            Rename = rename;
            Error = error;
        }

        public string Title { get; }

        public JobItem[] Items { get; }

        public string? Directory { get; }

        public string? Rename { get; }

        public OperationError? Error { get; }

        public static Planned Ready(string title, JobItem[] items, string? directory, string? rename, OperationError? error) =>
            new(title, items, directory, rename, error);

        public static Planned Invalid(string title, FileOpRequest request, OperationError error)
        {
            var name = request.Sources.Count > 0 ? request.Sources[0] : title;
            return new Planned(title, [new JobItem(name)], null, null, error);
        }
    }
}
