using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Favorites;

/// <summary>Saving the open tabs under a name, opening a saved set, and the Show menu entry that lists them.</summary>
public sealed class FavoriteTabsCommands
{
    private readonly PanelWorkspace _panels;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;

    public FavoriteTabsCommands(PanelWorkspace panels, AppSettings settings, IDialogService dialogs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _panels = panels;
        _settings = settings;
        _dialogs = dialogs;
        _text = text;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.SaveFavoriteTabs, () => _ = SaveAsync());
        shell.Handle(CommandId.RestoreFavoriteTabs1, () => _ = RestoreFirstAsync());
        shell.AppendDynamicItems("menu.show", () => MenuItems(shell));
    }

    /// <summary>Asks for a name (and for a go-ahead when it is taken), then stores the open tabs under it.</summary>
    public async Task SaveAsync()
    {
        var name = (await _dialogs.AskTextAsync(_text.Get("favorites.title"), _text.Get("favorites.name_prompt")).ConfigureAwait(true))?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var favorites = _settings.FavoriteTabs.Current;
        if (favorites.Find(name) is not null)
        {
            var message = _text.Get("favorites.overwrite").Replace("{name}", name, StringComparison.Ordinal);
            if (!await _dialogs.ConfirmAsync(_text.Get("favorites.title"), message).ConfigureAwait(true))
            {
                return;
            }
        }

        var set = _panels.CaptureFavorites(name);
        if (set is null)
        {
            return;
        }

        favorites.Save(set);
        _settings.FavoriteTabs.Save(favorites);
    }

    /// <summary>Opens the set called <paramref name="name"/> the way the settings choose (replace or add). An unknown name does nothing.</summary>
    public async Task RestoreAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var set = _settings.FavoriteTabs.Current.Find(name);
        if (set is not null)
        {
            await _panels.RestoreFavoritesAsync(set, _settings.Preferences.Current.FavoriteTabsMode).ConfigureAwait(true);
        }
    }

    /// <summary>Opens the first saved set; tells the user when there is none.</summary>
    public async Task RestoreFirstAsync()
    {
        var first = _settings.FavoriteTabs.Current.Sets.FirstOrDefault();
        if (first is null)
        {
            await _dialogs.ShowMessageAsync(_text.Get("favorites.title"), _text.Get("favorites.none")).ConfigureAwait(true);
            return;
        }

        await RestoreAsync(first.Name).ConfigureAwait(true);
    }

    /// <summary>The "Favorite tabs" submenu: the saved sets, then Save and Manage after a rule.</summary>
    public IReadOnlyList<ShellAction> MenuItems(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        var children = new List<ShellAction>();
        foreach (var set in _settings.FavoriteTabs.Current.Sets)
        {
            var name = set.Name;
            children.Add(new ShellAction(name, () => _ = RestoreAsync(name)));
        }

        if (children.Count > 0)
        {
            children.Add(ShellAction.Rule);
        }

        children.Add(new ShellAction(_text.Get("favorites.save_item"), () => shell.Dispatch(CommandId.SaveFavoriteTabs)));
        children.Add(new ShellAction(_text.Get("favorites.manage_item"), () => shell.Dispatch(CommandId.ManageFavoriteTabs)));
        return [new ShellAction(_text.Get("favorites.menu"), () => { }, Children: children)];
    }
}
