using System.IO.Enumeration;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Search;

namespace D3skCommander.Core.Index;

/// <summary>
/// Walks the roots once and returns a <see cref="NameIndex"/>. Links are not followed (a link is listed as a file
/// of size 0), an excluded folder is neither listed nor entered, and a root that cannot be read is skipped.
/// </summary>
public static class IndexScanner
{
    private const int ReportEvery = 1024;

    private static readonly EnumerationOptions Quiet = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>The roots of <paramref name="options"/>, or every mounted volume when it names none; each once.</summary>
    public static IReadOnlyList<string> ResolveRoots(IndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var wanted = options.Roots.Count > 0 ? options.Roots : DriveList.Mounted().Select(static drive => drive.Path).ToList();
        var roots = new List<string>();
        foreach (var root in wanted)
        {
            var full = Path.GetFullPath(root);
            if (!roots.Contains(full, Comparer))
            {
                roots.Add(full);
            }
        }

        return roots;
    }

    /// <summary>Cancelling throws <see cref="OperationCanceledException"/>. Progress is the number of entries so far.</summary>
    public static NameIndex Build(
        IndexOptions options,
        IProgress<long>? progress = null,
        CancellationToken cancel = default,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var roots = ResolveRoots(options);
        var walk = new Walk(Excluded(options.Excludes), roots, progress, cancel);
        foreach (var root in roots)
        {
            walk.Root(root);
        }

        return walk.Finish((time ?? TimeProvider.System).GetUtcNow());
    }

    private static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>"*/name" means a folder called name anywhere, which is what a name mask already says.</summary>
    private static ExcludedFolders Excluded(IReadOnlyList<string> entries) =>
        new(entries.Select(static entry =>
            entry.StartsWith("*/", StringComparison.Ordinal) || entry.StartsWith("*\\", StringComparison.Ordinal) ? entry[2..] : entry).ToList());

    private sealed class Walk(ExcludedFolders excluded, IReadOnlyList<string> roots, IProgress<long>? progress, CancellationToken cancel)
    {
        private readonly NameIndexBuilder _builder = new();
        private readonly Stack<(int Id, string Path)> _pending = new();

        public void Root(string path)
        {
            cancel.ThrowIfCancellationRequested();
            if (!IsReadable(path))
            {
                return;
            }

            _pending.Push((_builder.Add(path, -1, 0, Modified(path), true), path));
            while (_pending.Count > 0)
            {
                var (id, folder) = _pending.Pop();
                List(id, folder);
            }
        }

        public NameIndex Finish(DateTimeOffset builtAt)
        {
            progress?.Report(_builder.Count);
            return _builder.Build(builtAt);
        }

        private void List(int parent, string folder)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                foreach (var item in Children(folder))
                {
                    Add(parent, folder, item);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Listed so far stays; the rest of this folder is unreadable.
            }
        }

        private void Add(int parent, string folder, Item item)
        {
            if (item.IsFolder)
            {
                var path = Path.Join(folder, item.Name);
                if (excluded.Contains(item.Name, path) || IsRoot(path))
                {
                    return;
                }

                _pending.Push((_builder.Add(item.Name, parent, 0, item.Modified, true), path));
            }
            else
            {
                _builder.Add(item.Name, parent, item.Length, item.Modified, false);
            }

            if (_builder.Count % ReportEvery == 0)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Report(_builder.Count);
            }
        }

        // A volume mounted inside another is walked as a root of its own.
        private bool IsRoot(string path) => roots.Contains(path, Comparer);
    }

    private static bool IsReadable(string path)
    {
        try
        {
            using var probe = new FileSystemEnumerable<int>(path, static (ref FileSystemEntry _) => 0, new EnumerationOptions { IgnoreInaccessible = false })
                .GetEnumerator();
            probe.MoveNext();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DateTimeOffset Modified(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    private static FileSystemEnumerable<Item> Children(string folder) =>
        new(folder, static (ref FileSystemEntry entry) => Describe(ref entry), Quiet);

    private static Item Describe(ref FileSystemEntry entry)
    {
        var link = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
        var folder = entry.IsDirectory && !link;
        return new Item(entry.FileName.ToString(), folder, folder || link ? 0 : entry.Length, entry.LastWriteTimeUtc);
    }

    private readonly record struct Item(string Name, bool IsFolder, long Length, DateTimeOffset Modified);
}
