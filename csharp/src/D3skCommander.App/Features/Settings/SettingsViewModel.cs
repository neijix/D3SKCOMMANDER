using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Settings;

/// <summary>
/// The settings window's draft of <see cref="Preferences"/>. Apply copies the stored preferences, sets
/// the fields shown here and saves; the dark-mode field is not shown (the app follows the OS theme).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IShellWidget
{
    public const decimal MinFontSize = 8;

    public const decimal MaxFontSize = 40;

    private readonly AppSettings _settings;
    private readonly IOperationLog _log;
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly Action _changeFolder;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private decimal? _fontSize;

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private bool _useTrash;

    [ObservableProperty]
    private bool _confirmDelete;

    [ObservableProperty]
    private bool _confirmTrashFallback;

    [ObservableProperty]
    private bool _logOperations;

    [ObservableProperty]
    private bool _autoFolderSizes;

    [ObservableProperty]
    private bool _checkUpdates;

    [ObservableProperty]
    private string _editor = "";

    [ObservableProperty]
    private string _terminal = "";

    [ObservableProperty]
    private bool _showExtension;

    [ObservableProperty]
    private bool _showSize;

    [ObservableProperty]
    private bool _showDate;

    [ObservableProperty]
    private bool _showAttributes;

    [ObservableProperty]
    private UserCommandRow? _selectedCommand;

    [ObservableProperty]
    private string _note = "";

    public SettingsViewModel(
        AppSettings settings,
        IOperationLog log,
        ILocalizer text,
        IDialogService dialogs,
        Action changeFolder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(changeFolder);
        _settings = settings;
        _log = log;
        _text = text;
        _dialogs = dialogs;
        _changeFolder = changeFolder;
        Labels = new SettingsLabels(text);
        Load(settings.Preferences.Current);
    }

    public string? Name => "settings";

    public SettingsLabels Labels { get; }

    public ObservableCollection<UserCommandRow> Commands { get; } = [];

    public IReadOnlyList<string> Languages => [Labels.LanguageEnglish, Labels.LanguageFrench];

    public string FolderText => _text.Get("settings.folder_current").Replace("{path}", _settings.Directory, StringComparison.Ordinal);

    /// <summary>Raised by OK: the window closes itself.</summary>
    public event Action? CloseRequested;

    public Control CreateContent() => new SettingsView(this);

    /// <summary>Saves the draft over a copy of the stored preferences and applies the live parts.</summary>
    [RelayCommand]
    public void Apply()
    {
        var next = PreferencesCopy.Of(_settings.Preferences.Current);
        var language = LanguageIndex == 1 ? Language.Fr : Language.En;
        var languageChanged = language != next.Language;
        next.Language = language;
        next.FontSize = (float)Math.Clamp(FontSize ?? (decimal)Preferences.DefaultFontSize, MinFontSize, MaxFontSize);
        next.ShowHidden = ShowHidden;
        next.UseTrash = UseTrash;
        next.ConfirmDelete = ConfirmDelete;
        next.ConfirmTrashFallback = ConfirmTrashFallback;
        next.LogOperations = LogOperations;
        next.AutoFolderSizes = AutoFolderSizes;
        next.CheckUpdates = CheckUpdates;
        next.Editor = Editor.Trim();
        next.Terminal = Terminal.Trim();
        next.Columns = new Columns(ShowExtension, ShowSize, ShowDate, ShowAttributes);
        next.UserCommands = Commands.Select(row => row.Command).ToList();
        _settings.Preferences.Save(next);
        _log.Enabled = next.LogOperations;
        if (languageChanged)
        {
            _text.Use(language);
            Note = _text.Get("settings.restart_language");
            return;
        }

        Note = _text.Get("settings.saved");
    }

    [RelayCommand]
    public void Ok()
    {
        Apply();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    public void ChangeFolder() => _changeFolder();

    [RelayCommand]
    public async Task AddCommandAsync()
    {
        var made = await AskCommandAsync(null).ConfigureAwait(true);
        if (made is not null)
        {
            var row = new UserCommandRow(made);
            Commands.Add(row);
            SelectedCommand = row;
        }
    }

    [RelayCommand]
    public async Task EditCommandAsync()
    {
        var row = SelectedCommand;
        if (row is null)
        {
            return;
        }

        var made = await AskCommandAsync(row.Command).ConfigureAwait(true);
        if (made is not null)
        {
            var index = Commands.IndexOf(row);
            Commands[index] = new UserCommandRow(made);
            SelectedCommand = Commands[index];
        }
    }

    [RelayCommand]
    public void RemoveCommand()
    {
        var row = SelectedCommand;
        if (row is not null)
        {
            Commands.Remove(row);
        }
    }

    private async Task<UserCommand?> AskCommandAsync(UserCommand? current)
    {
        var name = new TextDialogField(_text.Get("settings.cmd_name"), current?.Name ?? "");
        var command = new TextDialogField(_text.Get("settings.cmd_command"), current?.Command ?? "");
        var once = new CheckDialogField(_text.Get("settings.once"), current?.OncePerFile ?? false);
        var form = new DialogForm(
            _text.Get("settings.cmd_title"),
            [name, command, once],
            _ => name.Text.Trim().Length > 0 && command.Text.Trim().Length > 0);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            return null;
        }

        return new UserCommand(name.Text.Trim(), command.Text.Trim(), once.IsChecked);
    }

    private void Load(Preferences current)
    {
        LanguageIndex = current.Language == Language.Fr ? 1 : 0;
        FontSize = (decimal)current.FontSize;
        ShowHidden = current.ShowHidden;
        UseTrash = current.UseTrash;
        ConfirmDelete = current.ConfirmDelete;
        ConfirmTrashFallback = current.ConfirmTrashFallback;
        LogOperations = current.LogOperations;
        AutoFolderSizes = current.AutoFolderSizes;
        CheckUpdates = current.CheckUpdates;
        Editor = current.Editor;
        Terminal = current.Terminal;
        ShowExtension = current.Columns.Extension;
        ShowSize = current.Columns.Size;
        ShowDate = current.Columns.Date;
        ShowAttributes = current.Columns.Attributes;
        foreach (var command in current.UserCommands)
        {
            Commands.Add(new UserCommandRow(command));
        }
    }
}
