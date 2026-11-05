using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Sync;

/// <summary>
/// One side's entries keyed by relative path. An unreadable directory fails the scan:
/// a silently missing subtree would be offered for deletion on the other side.
/// </summary>
internal static class SyncScan
{
    internal static FileResult<Dictionary<string, SyncEntry>> Read(
        string root,
        SyncOptions options,
        Mask mask,
        CancellationToken cancellationToken)
    {
        var rejected = RejectRoot(root);
        if (rejected is not null)
        {
            return FileResult<Dictionary<string, SyncEntry>>.Fail(rejected);
        }

        var entries = new Dictionary<string, SyncEntry>(StringComparer.Ordinal);
        var error = Walk(root, options, mask, entries, cancellationToken);
        if (error is not null)
        {
            return FileResult<Dictionary<string, SyncEntry>>.Fail(error);
        }

        return FileResult<Dictionary<string, SyncEntry>>.Ok(entries);
    }

    private static OperationError? RejectRoot(string root)
    {
        if (Directory.Exists(root))
        {
            return null;
        }

        if (File.Exists(root))
        {
            return OperationError.InvalidData("not a directory: " + root, root);
        }

        return OperationError.NotFound(root);
    }

    private static OperationError? Walk(
        string root,
        SyncOptions options,
        Mask mask,
        Dictionary<string, SyncEntry> entries,
        CancellationToken cancellationToken)
    {
        var maxDepth = options.Subdirs ? int.MaxValue : 1;
        var pending = new Stack<ScanNode>();
        pending.Push(new ScanNode(root, "", 1));
        while (pending.Count > 0)
        {
            var error = Visit(pending.Pop(), maxDepth, options, mask, entries, pending, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private static OperationError? Visit(
        ScanNode node,
        int maxDepth,
        SyncOptions options,
        Mask mask,
        Dictionary<string, SyncEntry> entries,
        Stack<ScanNode> pending,
        CancellationToken cancellationToken)
    {
        if (node.Depth > maxDepth)
        {
            return null;
        }

        try
        {
            foreach (var full in Directory.EnumerateFileSystemEntries(node.Full))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return OperationError.Cancelled(node.Full);
                }

                var error = Consider(full, node, maxDepth, options, mask, entries, pending, cancellationToken);
                if (error is not null)
                {
                    return error;
                }
            }
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return OperationError.FromException(ex, node.Full);
        }

        return null;
    }

    private static OperationError? Consider(
        string full,
        ScanNode parent,
        int maxDepth,
        SyncOptions options,
        Mask mask,
        Dictionary<string, SyncEntry> entries,
        Stack<ScanNode> pending,
        CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(full);
        var rel = parent.Relative.Length == 0 ? name : Path.Combine(parent.Relative, name);
        var described = Describe(full, options);
        if (described.Error is not null)
        {
            return described.Error;
        }

        if (described.Skip || (!described.IsDir && !mask.Matches(name)))
        {
            return null;
        }

        entries[rel] = new SyncEntry(described.IsDir, new SyncSide(described.Size, described.Modified), described.Empty);
        if (described.IsDir && parent.Depth < maxDepth)
        {
            pending.Push(new ScanNode(full, rel, parent.Depth + 1));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationError.Cancelled(full);
        }

        return null;
    }

    private static Described Describe(string full, SyncOptions options)
    {
        try
        {
            var info = new FileInfo(full);
            if (info.LinkTarget is not null)
            {
                return DescribeLink(info);
            }

            if (IsDirectory(info))
            {
                return DescribeDirectory(full, options);
            }

            return DescribeFile(info);
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return Described.Failed(ex, full);
        }
    }

    // A symlink counts as the file it points at. A dangling link, or a link to a directory, is skipped.
    private static Described DescribeLink(FileInfo link)
    {
        FileSystemInfo? target;
        try
        {
            target = link.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return Described.Skipped;
        }

        if (target is not FileInfo file || IsDirectory(file))
        {
            return Described.Skipped;
        }

        try
        {
            return DescribeFile(file);
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return Described.Skipped;
        }
    }

    private static Described DescribeDirectory(string full, SyncOptions options)
    {
        var empty = false;
        if (options.EmptyDirs)
        {
            try
            {
                empty = !Directory.EnumerateFileSystemEntries(full).Any();
            }
            catch (Exception ex) when (SyncIo.IsFileSystem(ex))
            {
                return Described.Failed(ex, full);
            }
        }

        return new Described(isDir: true, empty, 0, Modified(full), null);
    }

    private static Described DescribeFile(FileInfo info) =>
        new(isDir: false, empty: false, (ulong)info.Length, Modified(info.FullName), null);

    private static DateTimeOffset? Modified(string full)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(full), TimeSpan.Zero);
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return null;
        }
    }

    private static bool IsDirectory(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.Directory) != 0;

    private readonly struct ScanNode
    {
        public ScanNode(string full, string relative, int depth)
        {
            Full = full;
            Relative = relative;
            Depth = depth;
        }

        public string Full { get; }

        public string Relative { get; }

        public int Depth { get; }
    }

    private readonly struct Described
    {
        public Described(bool isDir, bool empty, ulong size, DateTimeOffset? modified, OperationError? error)
        {
            IsDir = isDir;
            Empty = empty;
            Size = size;
            Modified = modified;
            Error = error;
        }

        public static Described Skipped { get; } = new(false, false, 0, null, null) { Skip = true };

        public bool Skip { get; private init; }

        public bool IsDir { get; }

        public bool Empty { get; }

        public ulong Size { get; }

        public DateTimeOffset? Modified { get; }

        public OperationError? Error { get; }

        public static Described Failed(Exception exception, string path) =>
            new(false, false, 0, null, OperationError.FromException(exception, path));
    }
}
