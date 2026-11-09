using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

/// <summary>
/// The target of the context menu whose command is being dispatched. Handlers read it at their start (before
/// their first await) and fall back on the cursor entry when it is null, so a menu-bar or key dispatch behaves as before.
/// </summary>
public sealed partial class PanelWorkspace
{
    private PanelContextTarget? _contextTarget;

    /// <summary>The target of the context menu being dispatched, or null outside one.</summary>
    public PanelContextTarget? ContextTarget => Volatile.Read(ref _contextTarget);

    /// <summary>Makes <paramref name="target"/> the context target until the result is disposed; wrap one dispatch in it.</summary>
    public IDisposable UseContextTarget(PanelContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Volatile.Write(ref _contextTarget, target);
        return new Scope(this, target);
    }

    /// <summary>Open: what Enter does on the target entry (the cursor entry outside a context menu).</summary>
    public async Task OpenEntryAsync(CancellationToken cancellationToken = default)
    {
        var target = ContextTarget;
        var side = _active;
        if (target is null)
        {
            var file = FileToOpen(side);
            if (file is not null && OpenFile is { } open)
            {
                open(file);
                return;
            }

            await EnterAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (target.Kind == ContextTargetKind.EmptySpace || string.IsNullOrEmpty(target.EntryPath))
        {
            await ParentAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var path = target.EntryPath;
        if (target.Kind == ContextTargetKind.Folder)
        {
            await GoAsync(side, path, cancellationToken).ConfigureAwait(false);
            return;
        }

        var archive = target.Kind == ContextTargetKind.ArchiveFile;
        if (OpenFile is { } opener && (!archive || HasOpenAssociation?.Invoke(path) == true) && File.Exists(path))
        {
            opener(path);
            return;
        }

        if (archive)
        {
            await GoAsync(side, path, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>OpenInNewTab: the target folder in a new tab of the active side.</summary>
    public async Task OpenInNewTabAsync(CancellationToken cancellationToken = default)
    {
        var side = _active;
        var folder = FolderToOpen(ContextTarget, side);
        if (folder is null)
        {
            return;
        }

        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var panel = side == PanelSide.Left ? _left : _right;
            if (panel is null)
            {
                return;
            }

            var before = panel.Tabs.Count;
            await Task.Run(
                async () => await panel.NewTabAtAsync(new Location(folder), _showHidden, linked.Token).ConfigureAwait(false),
                linked.Token).ConfigureAwait(false);
            if (panel.Tabs.Count == before)
            {
                Fail(side, OperationError.NotFound(folder));
                return;
            }

            Remember(side);
            Publish(side, announce: false);
        }
        finally
        {
            _nav.Release();
        }
    }

    /// <summary>OpenInOtherPanel: the other panel goes to the target folder.</summary>
    public async Task OpenInOtherPanelAsync(CancellationToken cancellationToken = default)
    {
        var folder = FolderToOpen(ContextTarget, _active);
        if (folder is not null)
        {
            await GoAsync(Other(_active), folder, cancellationToken).ConfigureAwait(false);
        }
    }

    // The folder a target stands for: a folder row, the ".." row (the parent of the panel's folder), or null.
    private string? FolderToOpen(PanelContextTarget? target, PanelSide side)
    {
        if (target is not null)
        {
            return target.Kind switch
            {
                ContextTargetKind.Folder => target.EntryPath,
                ContextTargetKind.EmptySpace => ParentOf(Read(side).Path),
                _ => null,
            };
        }

        var snap = Read(side);
        if (snap.Cursor < 0 || snap.Cursor >= snap.Entries.Count)
        {
            return null;
        }

        var entry = snap.Entries[snap.Cursor];
        if (entry.IsParent)
        {
            return ParentOf(snap.Path);
        }

        return entry.IsDirectory ? entry.Path : null;
    }

    private static string? ParentOf(string folder) =>
        folder.Length == 0 ? null : new Location(folder).Parent()?.Path;

    private sealed class Scope(PanelWorkspace owner, PanelContextTarget target) : IDisposable
    {
        public void Dispose() => Interlocked.CompareExchange(ref owner._contextTarget, null, target);
    }
}

/// <summary>What a command dispatched from a context menu works on.</summary>
public static class ContextTargetPaths
{
    /// <summary>The marked set if any, else the row under the click, else (empty space) the panel folder.</summary>
    public static IReadOnlyList<string> Of(PanelContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.MarkedPaths.Count > 0)
        {
            return target.MarkedPaths;
        }

        return target.Kind != ContextTargetKind.EmptySpace && !string.IsNullOrEmpty(target.EntryPath)
            ? [target.EntryPath]
            : [target.PanelFolder];
    }
}
