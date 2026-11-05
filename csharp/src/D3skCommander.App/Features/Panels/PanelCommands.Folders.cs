using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelCommands
{
    /// <summary>Lists the saved folders; the chosen one is opened or removed.</summary>
    public async Task HotlistAsync()
    {
        var items = _settings.Hotlist.Current.Items;
        if (items.Count == 0)
        {
            await _dialogs.ShowMessageAsync(_text.Get("panels.hotlist"), _text.Get("panels.hotlistEmpty")).ConfigureAwait(true);
            return;
        }

        var labels = items.Select(item => item.Name + "  -  " + item.Path).ToList();
        var picked = await _dialogs.ChooseAsync(_text.Get("panels.hotlist"), _text.Get("panels.hotlistPick"), labels)
            .ConfigureAwait(true);
        if (picked is null)
        {
            return;
        }

        var item = items[picked.Value];
        var action = await _dialogs.ChooseAsync(
            item.Name,
            item.Path,
            [_text.Get("panels.go"), _text.Get("panels.remove")]).ConfigureAwait(true);
        if (action == 0)
        {
            await _panels.GoAsync(_panels.ActiveSide, item.Path).ConfigureAwait(true);
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
        hotlist.Items.RemoveAll(item => string.Equals(item.Path, path, StringComparison.Ordinal));
        hotlist.Items.Add(new HotlistItem(name.Trim(), path));
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

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length == 0 ? path : name;
    }
}
