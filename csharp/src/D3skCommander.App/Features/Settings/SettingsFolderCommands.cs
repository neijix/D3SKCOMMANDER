using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.App.Features.Settings;

/// <summary>The SettingsFolder command and the first-launch question (v0.1 <c>ask_settings_dir</c>).</summary>
public sealed class SettingsFolderCommands
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly Func<bool> _screenshotActive;
    private readonly Func<SettingsPlaces> _places;

    public SettingsFolderCommands(
        AppSettings settings,
        IDialogService dialogs,
        ILocalizer text,
        Func<bool> screenshotActive,
        Func<SettingsPlaces> places)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(screenshotActive);
        ArgumentNullException.ThrowIfNull(places);
        _settings = settings;
        _dialogs = dialogs;
        _text = text;
        _screenshotActive = screenshotActive;
        _places = places;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.SettingsFolder, () => _ = ChangeAsync());
        shell.OnStartup(() => _ = AskFirstLaunchAsync());
    }

    /// <summary>Asks for a new folder and moves the settings there. Returns true when the pointer was written.</summary>
    public async Task<bool> ChangeAsync()
    {
        var title = _text.Get("settings.folder");
        if (_settings.Folder.Source == SettingsSource.Env)
        {
            await _dialogs.ShowMessageAsync(title, _text.Get("settings.folder_env")).ConfigureAwait(true);
            return false;
        }

        var chosen = await AskAsync(title, _text.Get("settings.folder_prompt"), _settings.Directory).ConfigureAwait(true);
        if (chosen is null)
        {
            return false;
        }

        _settings.Flush();
        return await ApplyAsync(title, _settings.Directory, chosen).ConfigureAwait(true);
    }

    /// <summary>Asks once on a first launch, with the proposal of the install kind. False when it did not ask.</summary>
    public async Task<bool> AskFirstLaunchAsync()
    {
        if (!SettingsFolderMove.ShouldAskFirstLaunch(_settings.Folder, _screenshotActive()))
        {
            return false;
        }

        var title = _text.Get("settings.folder");
        var proposal = _places().DefaultDir();
        var chosen = await AskAsync(title, _text.Get("settings.folder_first"), proposal).ConfigureAwait(true);
        if (chosen is not null && !SameFolder(chosen, _settings.Directory))
        {
            await ApplyAsync(title, _settings.Directory, chosen).ConfigureAwait(true);
            return true;
        }

        // The default stays: write the preferences so the question is not asked again.
        _settings.Preferences.Save(PreferencesCopy.Of(_settings.Preferences.Current));
        _settings.Flush();
        return true;
    }

    private async Task<string?> AskAsync(string title, string prompt, string folder)
    {
        var field = new FolderDialogField(_text.Get("folder"), folder);
        var form = new DialogForm(title, [new NoteDialogField(prompt), field], _ => field.Path.Trim().Length > 0);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            return null;
        }

        return field.Path.Trim();
    }

    private async Task<bool> ApplyAsync(string title, string from, string to)
    {
        try
        {
            SettingsFolderMove.Run(from, to, _places());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var line = _text.Get("settings.folder_failed") + " " + ErrorLines.Describe(_text, ex, to);
            await _dialogs.ShowMessageAsync(title, line).ConfigureAwait(true);
            return false;
        }

        var done = _text.Get("settings.folder_moved").Replace("{path}", to, StringComparison.Ordinal);
        await _dialogs.ShowMessageAsync(title, done).ConfigureAwait(true);
        return true;
    }

    private static bool SameFolder(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
