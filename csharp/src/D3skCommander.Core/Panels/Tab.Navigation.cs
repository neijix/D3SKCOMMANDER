using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Panels;

public sealed partial class Tab
{
    /// <summary>
    /// Opens <paramref name="location"/> and records history.
    /// A folder that cannot be listed is not opened: the tab stays put and <see cref="Error"/> is set.
    /// </summary>
    public async Task<bool> GoAsync(Location location, bool showHidden, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        var listed = await ReadListingAsync(location, showHidden, false, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            Error = listed.Error ?? OperationError.NotFound(location.Path);
            return false;
        }

        var previous = Location;
        Location = location;
        Error = null;
        Filter = null;
        Branch = false;
        _folderSizes.Clear();
        _selected.Clear();
        Cursor = 0;
        ReplaceEntries(listed.Value);
        ApplyFilter();
        Remember(previous);
        return true;
    }

    public Task BackAsync(bool showHidden, CancellationToken cancellationToken = default) =>
        MoveHistoryAsync(_back, _forward, showHidden, cancellationToken);

    public Task ForwardAsync(bool showHidden, CancellationToken cancellationToken = default) =>
        MoveHistoryAsync(_forward, _back, showHidden, cancellationToken);

    public Location? ParentLocation() => Location.Parent();

    /// <summary>Where Enter on <paramref name="entry"/> leads. An archive file opens as a folder. Any other file leads nowhere.</summary>
    public Location? EnterLocation(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsParent)
        {
            return ParentLocation();
        }

        if (!entry.IsDirectory)
        {
            if (ArchiveFileSystemProvider.IsArchiveFile(entry.Path))
            {
                return new Location(entry.Path);
            }

            return null;
        }

        return new Location(entry.Path);
    }

    private void Remember(Location previous)
    {
        _back.Add(previous);
        _forward.Clear();
        SelectCameFrom(previous.Path);
    }

    private async Task MoveHistoryAsync(
        List<Location> from,
        List<Location> to,
        bool showHidden,
        CancellationToken cancellationToken)
    {
        if (from.Count == 0)
        {
            return;
        }

        var next = from[^1];
        var listed = await ReadListingAsync(next, showHidden, false, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            Error = listed.Error ?? OperationError.NotFound(next.Path);
            return;
        }

        var previous = Location;
        from.RemoveAt(from.Count - 1);
        to.Add(previous);
        Location = next;
        Error = null;
        Filter = null;
        Branch = false;
        _folderSizes.Clear();
        _selected.Clear();
        Cursor = 0;
        ReplaceEntries(listed.Value);
        ApplyFilter();
    }

    // Going up puts the cursor on the folder just left, not on "..".
    private void SelectCameFrom(string from)
    {
        var index = FindVisible(entry => CameFrom(entry, from));
        if (index is null)
        {
            return;
        }

        Cursor = index.Value;
        ScrollToCursor = true;
    }

    private static bool CameFrom(Entry entry, string from)
    {
        if (entry.IsParent || entry.Path.Length == 0)
        {
            return false;
        }

        if (string.Equals(entry.Path, from, StringComparison.Ordinal))
        {
            return true;
        }

        return EndsWithComponent(from, entry.Path);
    }

    private static bool EndsWithComponent(string path, string suffix)
    {
        if (!path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Length == suffix.Length)
        {
            return true;
        }

        var boundary = path[path.Length - suffix.Length - 1];
        return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
    }
}
