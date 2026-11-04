using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Panels;

/// <summary>
/// One panel tab: folder, listing, cursor, filter, sort and history.
/// Listing comes from <see cref="IFileSystemProvider"/>; nothing here draws.
/// </summary>
public sealed partial class Tab
{
    private readonly IFileSystemProvider _provider;
    private readonly TimeProvider _clock;
    private readonly List<Entry> _entries = [];
    private readonly List<int> _visible = [];
    private readonly Selection _selected = new();
    private readonly List<Location> _back = [];
    private readonly List<Location> _forward = [];
    private string _typeAhead = "";
    private long? _typeAheadAt;

    public Tab(Location location, SortOrder sort, IFileSystemProvider provider)
        : this(location, sort, provider, TimeProvider.System)
    {
    }

    public Tab(Location location, SortOrder sort, IFileSystemProvider provider, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        Location = location;
        Sort = sort;
        _provider = provider;
        _clock = clock;
        ScrollToCursor = true;
    }

    public Location Location { get; private set; }

    public SortOrder Sort { get; private set; }

    /// <summary>Index into <see cref="VisibleEntries"/>.</summary>
    public int Cursor { get; private set; }

    /// <summary>Quick filter text. Null shows every row. Call <see cref="ApplyFilter"/> after a change.</summary>
    public string? Filter { get; set; }

    /// <summary>Set when the folder could not be listed. The previous folder stays in <see cref="Location"/>.</summary>
    public OperationError? Error { get; private set; }

    public bool ScrollToCursor { get; set; }

    public int SelectedCount => _selected.Count;

    public Entry? Current
    {
        get
        {
            if (Cursor < 0 || Cursor >= _visible.Count)
            {
                return null;
            }

            return EntryAt(_visible[Cursor]);
        }
    }

    public IEnumerable<Entry> VisibleEntries
    {
        get
        {
            foreach (var index in _visible)
            {
                var entry = EntryAt(index);
                if (entry is not null)
                {
                    yield return entry;
                }
            }
        }
    }

    public async Task ReloadAsync(bool showHidden, CancellationToken cancellationToken = default)
    {
        var keep = Current?.Path;
        Error = null;
        var listed = await ReadListingAsync(showHidden, cancellationToken).ConfigureAwait(false);
        ReplaceEntries(listed);
        _selected.Retain(_entries.Select(entry => entry.Path));
        ApplyFilter();
        if (keep is not null)
        {
            SelectPath(keep);
        }

        ClampCursor();
    }

    public void ApplyFilter()
    {
        var mask = FilterMask();
        _visible.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            if (Shown(_entries[i], mask))
            {
                _visible.Add(i);
            }
        }

        ClampCursor();
    }

    public void SetSort(SortKey key)
    {
        if (Sort.Key == key)
        {
            Sort = new SortOrder(key, !Sort.Descending);
        }
        else
        {
            Sort = new SortOrder(key, false);
        }

        var keep = Current?.Path;
        LocalFileSystem.Sort(_entries, Sort);
        ApplyFilter();
        if (keep is not null)
        {
            SelectPath(keep);
        }
    }

    public void MoveCursor(int delta)
    {
        var length = _visible.Count;
        if (length == 0)
        {
            return;
        }

        var next = Cursor + delta;
        if (next < 0)
        {
            next = 0;
        }

        if (next >= length)
        {
            next = length - 1;
        }

        Cursor = next;
        ScrollToCursor = true;
    }

    /// <summary>Jumps to the next visible name starting with the typed letters. A pause over 900 ms starts over.</summary>
    public void TypeAhead(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var now = _clock.GetTimestamp();
        if (TypeAheadExpired(now))
        {
            _typeAhead = "";
        }

        _typeAhead += text.ToLowerInvariant();
        _typeAheadAt = now;
        var index = FindVisible(entry =>
            !entry.IsParent && entry.Name.StartsWith(_typeAhead, StringComparison.OrdinalIgnoreCase));
        if (index is null)
        {
            return;
        }

        Cursor = index.Value;
        ScrollToCursor = true;
    }

    private async Task<FileResult<List<Entry>>> ReadListingAsync(bool showHidden, CancellationToken cancellationToken)
    {
        var entries = new List<Entry>();
        await foreach (var item in _provider.ListAsync(Location.Path, showHidden, cancellationToken).ConfigureAwait(false))
        {
            if (!item.IsSuccess)
            {
                return FileResult<List<Entry>>.Fail(item.Error ?? OperationError.NotFound(Location.Path));
            }

            entries.Add(item.Value);
        }

        return FileResult<List<Entry>>.Ok(entries);
    }

    private void ReplaceEntries(FileResult<List<Entry>> listed)
    {
        _entries.Clear();
        if (!listed.IsSuccess)
        {
            Error = listed.Error ?? OperationError.NotFound(Location.Path);
            return;
        }

        _entries.AddRange(listed.Value);
        LocalFileSystem.Sort(_entries, Sort);
    }

    // Plain text means "contains". A * or ? is used as a mask.
    private Mask? FilterMask()
    {
        if (Filter is null)
        {
            return null;
        }

        if (Filter.Contains('*') || Filter.Contains('?'))
        {
            return new Mask(Filter);
        }

        return new Mask("*" + Filter + "*");
    }

    private static bool Shown(Entry entry, Mask? mask)
    {
        if (entry.IsParent || mask is null)
        {
            return true;
        }

        return mask.Matches(entry.Name);
    }

    private void SelectPath(string path)
    {
        var index = FindVisible(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));
        if (index is null)
        {
            return;
        }

        Cursor = index.Value;
        ScrollToCursor = true;
    }

    private void ClampCursor()
    {
        if (Cursor < _visible.Count)
        {
            return;
        }

        var last = _visible.Count - 1;
        Cursor = last < 0 ? 0 : last;
    }

    private bool TypeAheadExpired(long now)
    {
        if (_typeAheadAt is null)
        {
            return true;
        }

        return _clock.GetElapsedTime(_typeAheadAt.Value, now) > TimeSpan.FromMilliseconds(900);
    }

    private int? FindVisible(Func<Entry, bool> match)
    {
        for (var i = 0; i < _visible.Count; i++)
        {
            var entry = EntryAt(_visible[i]);
            if (entry is not null && match(entry))
            {
                return i;
            }
        }

        return null;
    }

    private Entry? EntryAt(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return null;
        }

        return _entries[index];
    }
}
