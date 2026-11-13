using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelCommands
{
    /// <summary>Lists the favorites (files and folders); the chosen one is opened or removed, or reported gone.</summary>
    public async Task HotlistAsync()
    {
        var items = _settings.Hotlist.Current.Items;
        if (items.Count == 0)
        {
            await _dialogs.ShowMessageAsync(_text.Get("panels.hotlist"), _text.Get("panels.hotlistEmpty")).ConfigureAwait(true);
            return;
        }

        var snapshot = items.ToList();
        var targets = await Task.Run(() => snapshot.Select(Probe).ToList()).ConfigureAwait(true);
        var labels = snapshot.Select((item, index) => Label(item, targets[index])).ToList();
        var picked = await _dialogs.ChooseAsync(_text.Get("panels.hotlist"), _text.Get("panels.hotlistPick"), labels)
            .ConfigureAwait(true);
        if (picked is null)
        {
            return;
        }

        var item = snapshot[picked.Value];
        var target = await Task.Run(() => Probe(item)).ConfigureAwait(true);
        if (target.Kind == HotlistTargetKind.Missing)
        {
            var remove = await _dialogs.ChooseAsync(
                item.Name,
                _text.Get("panels.favoriteMissing").Replace("{0}", item.Path, StringComparison.Ordinal),
                [_text.Get("panels.remove")]).ConfigureAwait(true);
            if (remove == 0)
            {
                RemoveHotlist(item);
            }

            return;
        }

        var action = await _dialogs.ChooseAsync(
            item.Name,
            item.Path,
            [_text.Get("panels.go"), _text.Get("panels.remove")]).ConfigureAwait(true);
        if (action == 0)
        {
            if (target.Kind == HotlistTargetKind.File)
            {
                await _panels.RevealAsync(target.Path).ConfigureAwait(true);
            }
            else
            {
                await _panels.GoAsync(_panels.ActiveSide, target.Folder).ConfigureAwait(true);
            }
        }
        else if (action == 1)
        {
            RemoveHotlist(item);
        }
    }

    /// <summary>Saves the folder of the active panel under a name the user types.</summary>
    public async Task AddHotlistAsync()
    {
        var path = _panels.Read(_panels.ActiveSide).Path;
        if (path.Length == 0)
        {
            return;
        }

        var name = await _dialogs.AskTextAsync(
            _text.Get("panels.hotlistAdd"),
            _text.Get("panels.name"),
            FolderName(path)).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var hotlist = _settings.Hotlist.Current;
        hotlist.Add(new HotlistItem(name.Trim(), path));
        _settings.Hotlist.Save(hotlist);
    }

    /// <summary>
    /// Saves the context target (the item under the cursor, or the panel folder for empty space) as a favorite under a
    /// name the user types. Local paths only (also the files of a results or branch list): a remote or archive panel adds nothing.
    /// </summary>
    public async Task AddToFavoritesAsync()
    {
        var context = _panels.ContextTarget ?? _panels.ContextTargetFor(_panels.ActiveSide, false);
        if (context is null || context.Provider is not (PanelProviderKind.Local or PanelProviderKind.FlatList))
        {
            return;
        }

        // A flat list's pseudo folder is no place: only its entries (real local paths) can be saved.
        var path = context.Kind == ContextTargetKind.EmptySpace
            ? context.Provider == PanelProviderKind.Local ? context.PanelFolder : null
            : context.EntryPath;

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var name = await _dialogs.AskTextAsync(
            _text.Get("panels.favoriteAdd"),
            _text.Get("panels.name"),
            FolderName(path)).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var hotlist = _settings.Hotlist.Current;
        hotlist.Add(new HotlistItem(name.Trim(), path));
        _settings.Hotlist.Save(hotlist);
    }

    /// <summary>Lists the recent folders; the chosen one is opened in the active panel.</summary>
    public async Task HistoryAsync()
    {
        var history = _panels.History;
        if (history.Count == 0)
        {
            await _dialogs.ShowMessageAsync(_text.Get("panels.history"), _text.Get("panels.historyEmpty")).ConfigureAwait(true);
            return;
        }

        var picked = await _dialogs.ChooseAsync(_text.Get("panels.history"), _text.Get("panels.historyPick"), history)
            .ConfigureAwait(true);
        if (picked is not null)
        {
            await _panels.GoAsync(_panels.ActiveSide, history[picked.Value]).ConfigureAwait(true);
        }
    }

    private void RemoveHotlist(HotlistItem item)
    {
        var hotlist = _settings.Hotlist.Current;
        hotlist.Items.Remove(item);
        _settings.Hotlist.Save(hotlist);
    }

    private static HotlistTarget Probe(HotlistItem item) => HotlistTarget.Resolve(item, UncPath.FolderExists, File.Exists);

    private string Label(HotlistItem item, HotlistTarget target)
    {
        var kind = target.Kind switch
        {
            HotlistTargetKind.Folder => _text.Get("panels.favoriteFolder"),
            HotlistTargetKind.File => _text.Get("panels.favoriteFile"),
            _ => "",
        };
        return item.Name + (kind.Length == 0 ? "" : " (" + kind + ")") + "  -  " + item.Path;
    }

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length == 0 ? path : name;
    }
}
