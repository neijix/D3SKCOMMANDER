using D3skCommander.App.Shell;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    /// <summary>Marks (or unmarks) the files of the active panel that match <paramref name="mask"/>.</summary>
    public void SelectGroup(string mask, bool select)
    {
        ArgumentNullException.ThrowIfNull(mask);
        EditSelection(tab => tab.SelectMask(mask, select, includeDirectories: false));
    }

    public void SelectSameExtension() => EditSelection(tab => tab.SelectSameExtension());

    /// <summary>The names (or full paths) of the targets of the active panel, one per line. Null while a listing is in progress.</summary>
    public string? CopyText(bool paths)
    {
        if (!TryEnter())
        {
            return null;
        }

        try
        {
            var targets = TabOf(_active)?.Targets();
            if (targets is null)
            {
                return null;
            }

            return paths ? PanelText.Paths(targets) : PanelText.Names(targets);
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>Shows the quick-filter box on the active panel. The text starts empty and keeps every row.</summary>
    public void OpenFilter()
    {
        EditSelection(tab => tab.SetFilter(tab.Filter ?? ""));
    }

    /// <summary>Narrows the active panel to the rows matching <paramref name="text"/>; null closes the filter. False when a listing is busy.</summary>
    public bool SetFilter(PanelSide side, string? text)
    {
        if (!TryEnter())
        {
            return false;
        }

        try
        {
            var tab = TabOf(side);
            if (tab is null)
            {
                return false;
            }

            tab.SetFilter(text);
            Publish(side, announce: false);
            return true;
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>Switches the active panel between its folder and the flat list of every file below it.</summary>
    public async Task ToggleBranchAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var tab = TabOf(_active);
            if (tab is null)
            {
                return;
            }

            await Task.Run(
                async () => await tab.SetBranchAsync(!tab.Branch, _showHidden, linked.Token).ConfigureAwait(false),
                linked.Token).ConfigureAwait(false);
            Remember(_active);
            Publish(_active, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>The folder and the marked (else all) subfolders of the active panel, or null while a listing is in progress.</summary>
    public (PanelSide Side, string Directory, IReadOnlyList<string> Folders)? FolderTargets()
    {
        if (!TryEnter())
        {
            return null;
        }

        try
        {
            var tab = TabOf(_active);
            if (tab is null)
            {
                return null;
            }

            return (_active, tab.Location.Path, tab.FolderTargets().Select(entry => entry.Path).ToList());
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>Shows the size of <paramref name="folder"/> in <paramref name="side"/> while it still lists <paramref name="directory"/>.</summary>
    public async Task ApplyFolderSizeAsync(
        PanelSide side,
        string directory,
        string folder,
        ulong bytes,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var tab = TabOf(side);
            if (tab is null || tab.Location.Path != directory)
            {
                return;
            }

            tab.SetFolderSize(folder, bytes);
            Publish(side, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }
}
