using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Panels;

public sealed partial class Tab
{
    /// <summary>Most files a branch view lists.</summary>
    public const int BranchLimit = 100_000;

    /// <summary>True while the tab lists every file below the folder, named by its relative path.</summary>
    public bool Branch { get; private set; }

    /// <summary>Turns the flat branch view on or off and lists the folder again. Navigating leaves it.</summary>
    public async Task SetBranchAsync(bool on, bool showHidden, CancellationToken cancellationToken = default)
    {
        var before = Branch;
        Branch = on;
        await ReloadAsync(showHidden, cancellationToken).ConfigureAwait(false);
        if (Error is not null)
        {
            Branch = before;
        }
    }

    /// <summary>Sets the quick filter text (null shows every row) and narrows the list.</summary>
    public void SetFilter(string? text)
    {
        Filter = text;
        ApplyFilter();
    }

    /// <summary>Marks every file with the extension of the row under the cursor.</summary>
    public void SelectSameExtension()
    {
        var current = Current;
        if (current is null || current.IsParent || current.IsDirectory)
        {
            return;
        }

        var extension = current.Extension;
        foreach (var entry in VisibleEntries)
        {
            if (!entry.IsParent
                && !entry.IsDirectory
                && string.Equals(entry.Extension, extension, StringComparison.OrdinalIgnoreCase))
            {
                _selected.Add(entry.Path);
            }
        }
    }

    /// <summary>The marked folders, or every folder of the list when none is marked. ".." is never one.</summary>
    public IReadOnlyList<Entry> FolderTargets()
    {
        var marked = new List<Entry>();
        var all = new List<Entry>();
        foreach (var entry in VisibleEntries)
        {
            if (entry.IsParent || !entry.IsDirectory)
            {
                continue;
            }

            all.Add(entry);
            if (_selected.Contains(entry.Path))
            {
                marked.Add(entry);
            }
        }

        return marked.Count > 0 ? marked : all;
    }

    /// <summary>Shows <paramref name="bytes"/> in the size column of the folder <paramref name="path"/> until the tab leaves this folder.</summary>
    public void SetFolderSize(string path, ulong bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        _folderSizes[path] = bytes;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Path, path, StringComparison.Ordinal))
            {
                _entries[i] = WithKnownSize(_entries[i]);
            }
        }
    }

    private Entry WithKnownSize(Entry entry)
    {
        if (!entry.IsDirectory || entry.IsParent || !_folderSizes.TryGetValue(entry.Path, out var bytes))
        {
            return entry;
        }

        return new Entry(
            entry.Name,
            entry.Path,
            entry.IsDirectory,
            entry.IsLink,
            entry.IsParent,
            bytes,
            entry.Modified,
            entry.Hidden,
            entry.ReadOnly,
            entry.Attributes);
    }

    // The ".." row stays; every folder gives way to the files below it.
    private static List<Entry> FlatBranch(List<Entry> listed, string path, bool showHidden, CancellationToken cancellationToken)
    {
        var flat = new List<Entry>();
        foreach (var entry in listed)
        {
            if (entry.IsParent)
            {
                flat.Add(entry);
            }
        }

        flat.AddRange(LocalFileSystem.ListBranch(path, showHidden, BranchLimit, cancellationToken));
        return flat;
    }
}
