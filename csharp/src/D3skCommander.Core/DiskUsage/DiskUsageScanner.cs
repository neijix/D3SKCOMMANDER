namespace D3skCommander.Core.DiskUsage;

/// <summary>
/// Measures a folder in one walk and keeps a node per folder. Links are not followed (a link counts as a file of
/// size 0). Of each folder's files only the biggest <see cref="TopFiles"/> stay as nodes; the rest are summed.
/// </summary>
public static partial class DiskUsageScanner
{
    public const int TopFiles = 50;

    /// <summary>The tree below <paramref name="root"/>. Cancelling throws <see cref="OperationCanceledException"/>.</summary>
    public static SizeNode Scan(
        string root,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancel = default,
        int topFiles = TopFiles)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        var full = System.IO.Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(full);
        }

        var walk = new Walk(progress, cancel, Math.Max(0, topFiles), null);
        var node = walk.Folder(full, NameOf(full));
        walk.Report();
        return node;
    }

    private static string NameOf(string full)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(full));
        return name.Length == 0 ? full : name;
    }

    private sealed partial class Walk(IProgress<ScanProgress>? progress, CancellationToken cancel, int topFiles, HashSet<string>? skip)
    {
        private const int ReportEvery = 256;
        private static readonly EnumerationOptions Strict = new()
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        private long _files;
        private long _folders;
        private long _bytes;

        public SizeNode Folder(string path, string name)
        {
            cancel.ThrowIfCancellationRequested();
            var folders = new List<(string Path, string Name)>();
            var biggest = new PriorityQueue<(string Name, string Path, long Length), long>();
            long count = 0;
            long total = 0;
            var unreadable = false;
            try
            {
                foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Strict))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (IsRealFolder(info))
                    {
                        if (skip is not null && skip.Contains(Normal(info.FullName)))
                        {
                            continue;
                        }

                        folders.Add((info.FullName, info.Name));
                        continue;
                    }

                    var length = LengthOf(info);
                    count++;
                    total += length;
                    Keep(biggest, (info.Name, info.FullName, length));
                    Seen(length);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable = true;
            }

            var children = new List<SizeNode>(folders.Count + biggest.Count + 1);
            long kept = 0;
            while (biggest.TryDequeue(out var file, out _))
            {
                kept += file.Length;
                children.Add(SizeNode.File(file.Name, file.Path, file.Length));
            }

            if (count > children.Count)
            {
                children.Add(SizeNode.SmallFiles(path, count - children.Count, total - kept));
            }

            foreach (var (childPath, childName) in folders)
            {
                _folders++;
                children.Add(Folder(childPath, childName));
            }

            return SizeNode.Folder(name, path, children, unreadable);
        }

        public void Report() => progress?.Report(new ScanProgress(_files, _folders, _bytes));

        private void Keep(PriorityQueue<(string Name, string Path, long Length), long> biggest, (string, string, long) file)
        {
            biggest.Enqueue(file, file.Item3);
            if (biggest.Count > topFiles)
            {
                biggest.Dequeue();
            }
        }

        private void Seen(long length)
        {
            _files++;
            _bytes += length;
            if (_files % ReportEvery == 0)
            {
                Report();
            }
        }

        // The same rule as LocalFileSystem.IsRealDirectory: a link, even to a folder, is not walked.
        private static bool IsRealFolder(FileSystemInfo info)
        {
            try
            {
                return info is DirectoryInfo && (info.Attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static long LengthOf(FileSystemInfo info)
        {
            try
            {
                return info is FileInfo file && (info.Attributes & FileAttributes.ReparsePoint) == 0 ? file.Length : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
