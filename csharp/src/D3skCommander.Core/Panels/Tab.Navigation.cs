using D3skCommander.Core.FileSystem;

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
        var previous = Location;
        Location = location;
        Filter = null;
        _selected.Clear();
        Cursor = 0;
        await ReloadAsync(showHidden, cancellationToken).ConfigureAwait(false);
        if (Error is null)
        {
            Remember(previous);
            return true;
        }

        await RestoreAsync(previous, showHidden).ConfigureAwait(false);
        return false;
    }

    public Task BackAsync(bool showHidden, CancellationToken cancellationToken = default) =>
        MoveHistoryAsync(_back, _forward, showHidden, cancellationToken);

    public Task ForwardAsync(bool showHidden, CancellationToken cancellationToken = default) =>
        MoveHistoryAsync(_forward, _back, showHidden, cancellationToken);

    public Location? ParentLocation() => Location.Parent();

    /// <summary>Where Enter on <paramref name="entry"/> leads. A file is not an archive here, so it leads nowhere.</summary>
    public Location? EnterLocation(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsParent)
        {
            return ParentLocation();
        }

        if (!entry.IsDirectory)
        {
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

    // Not cancelable: the navigation that failed may have failed because its token was cancelled,
    // and the folder the user stays in must be listed again either way.
    private async Task RestoreAsync(Location previous, bool showHidden)
    {
        var error = Error;
        Location = previous;
        await ReloadAsync(showHidden, CancellationToken.None).ConfigureAwait(false);
        Error = error;
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
        var previous = Location;
        Location = next;
        Cursor = 0;
        await ReloadAsync(showHidden, cancellationToken).ConfigureAwait(false);
        if (Error is not null)
        {
            // As GoAsync: a folder that cannot be listed is not opened, and the history is kept.
            await RestoreAsync(previous, showHidden).ConfigureAwait(false);
            return;
        }

        from.RemoveAt(from.Count - 1);
        to.Add(previous);
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
