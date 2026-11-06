using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

/// <summary>
/// Search results or a branch view, shown as one virtual folder. The rows are the entries
/// handed in (or <see cref="Branch"/>). Reads and changes of a real path go through
/// <see cref="LocalFileSystemProvider"/>; the folder itself is the snapshot.
/// </summary>
public sealed partial class ListFileSystemProvider : FileSystemProvider
{
    /// <summary>v0.1 branch view stopped after this many files.</summary>
    public const int BranchLimit = 200_000;

    private readonly string _directory;
    private readonly Entry[] _entries;
    private readonly LocalFileSystemProvider _local = new();
    private readonly bool _recheck;

    /// <param name="recheck">
    /// Look each path up again on every listing: a path that is gone drops out and the others show
    /// their current size and date (search results). A branch view keeps its snapshot.
    /// </param>
    public ListFileSystemProvider(string directory, IReadOnlyList<Entry> entries, bool recheck = false)
    {
        _recheck = recheck;
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(entries);
        _directory = directory;
        _entries = new Entry[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(entries[i]);
            _entries[i] = entries[i];
        }
    }

    public override bool IsReadOnly => false;

    public string Directory => _directory;

    /// <summary>The branch view of <paramref name="directory"/>: nested files, named by their relative path.</summary>
    public static ListFileSystemProvider Branch(string directory, bool showHidden, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var entries = LocalFileSystem.ListBranch(directory, showHidden, BranchLimit, cancellationToken);
        return new ListFileSystemProvider(directory, entries);
    }

    protected override async IAsyncEnumerable<FileResult<Entry>> ListCore(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        if (!Same(directory, _directory))
        {
            yield return FileResult<Entry>.Fail(OperationError.NotFound(directory));
            yield break;
        }

        foreach (var entry in Visible(showHidden))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
                yield break;
            }

            yield return FileResult<Entry>.Ok(entry);
        }
    }

    protected override Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken)
    {
        if (Same(path, _directory))
        {
            var live = LocalFileSystem.ReadEntry(path);
            if (live.IsSuccess)
            {
                return Task.FromResult(live);
            }

            return Task.FromResult(FileResult<Entry>.Ok(VirtualDirectory()));
        }

        return _local.GetEntryAsync(path, cancellationToken);
    }

    protected override Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken)
    {
        if (Same(path, _directory))
        {
            return Task.FromResult(FileResult<Stream>.Fail(OperationError.InvalidData("not a file", path)));
        }

        return _local.OpenReadAsync(path, cancellationToken);
    }

    private List<Entry> Visible(bool showHidden)
    {
        var found = new List<Entry>();
        AddParent(found);
        foreach (var entry in _entries)
        {
            if (entry.IsParent || Same(entry.Path, _directory))
            {
                continue;
            }

            if (showHidden || !entry.Hidden)
            {
                AddCurrent(found, entry);
            }
        }

        return found;
    }

    private void AddCurrent(List<Entry> found, Entry entry)
    {
        if (!_recheck)
        {
            found.Add(entry);
            return;
        }

        var live = LocalFileSystem.ReadEntry(entry.Path);
        if (live.IsSuccess)
        {
            live.Value.Name = entry.Name;
            found.Add(live.Value);
        }
    }

    private void AddParent(List<Entry> found)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_directory));
        if (string.IsNullOrEmpty(parent))
        {
            return;
        }

        found.Add(new Entry("..", parent, true, false, true, 0, null, false, false, ""));
    }

    private Entry VirtualDirectory()
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_directory));
        if (name.Length == 0)
        {
            name = _directory;
        }

        return new Entry(name, _directory, true, false, false, 0, null, false, false, "");
    }

    private static bool Same(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.Ordinal);
}
