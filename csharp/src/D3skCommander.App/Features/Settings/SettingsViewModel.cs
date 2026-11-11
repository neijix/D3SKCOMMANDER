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
    private string _indexRoots = "";

    [ObservableProperty]
    private string _indexExcludes = "";

    [ObservableProperty]
    private decimal? _indexRefreshHours;

    [ObservableProperty]
    private decimal? _previewStartSeconds;

    [ObservableProperty]
    private string _editor = "";

    [ObservableProperty]
    private string _terminal = "";

    [ObservableProperty]
    private UserCommandRow? _selectedCommand;

    [ObservableProperty]
    private string _note = "";

    [ObservableProperty]
    private SettingsCategoryItem _selectedCategory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isDirty;

    private bool _ready;

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
        ColumnSets = new ColumnSetsEditorModel(text, dialogs, settings.Preferences.Current.AllColumnSets());
        Associations = new AssociationsEditorModel(text, settings.Preferences.Current.Associations);
        Favorites = new FavoriteTabsEditorModel(text, dialogs, settings.FavoriteTabs.Current, settings.Preferences.Current.FavoriteTabsMode);
        Load(settings.Preferences.Current);
        Toolbar = new ToolbarEditorModel(text, settings.Preferences.Current.Toolbar, UserCommandNames());
        Categories = [.. SettingsCategories.All.Select(c => new SettingsCategoryItem(c.Id, text.Get(c.NameKey)))];
        _selectedCategory = Categories[0];
        Commands.CollectionChanged += (_, _) =>
        {
            Toolbar.SetUserCommandNames(UserCommandNames());
            RefreshDraftState();
        };
        ColumnSets.DraftChanged += OnEditorChanged;
        Associations.DraftChanged += OnEditorChanged;
        Favorites.DraftChanged += OnEditorChanged;
        Toolbar.DraftChanged += OnEditorChanged;
        _ready = true;
        RefreshDraftState();
    }

    private void OnEditorChanged(object? sender, EventArgs e) => RefreshDraftState();

    public string? Name => "settings";

    public SettingsLabels Labels { get; }

    public ObservableCollection<UserCommandRow> Commands { get; } = [];

    /// <summary>The Columns section: the column sets and their columns.</summary>
    public ColumnSetsEditorModel ColumnSets { get; }

    /// <summary>The File associations section: the rules that open, view and edit files with other programs.</summary>
    public AssociationsEditorModel Associations { get; }

    /// <summary>The Favorite tabs section: saved tab sets and the restore mode.</summary>
    public FavoriteTabsEditorModel Favorites { get; }

    /// <summary>The Toolbar section: the buttons of the main toolbar.</summary>
    public ToolbarEditorModel Toolbar { get; }

    /// <summary>The pages, in display order, with their localized names.</summary>
    public IReadOnlyList<SettingsCategoryItem> Categories { get; }

    /// <summary>Shows a page by its Core category id; an unknown id changes nothing. The draft is untouched.</summary>
    public void Select(string categoryId)
    {
        var found = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (found is not null)
        {
            SelectedCategory = found;
        }
    }

    /// <summary>Compares the draft with the live store again: the window calls this when it is activated.</summary>
    public void RefreshDraftState()
    {
        if (_ready)
        {
            IsDirty = PreferencesDraft.Differs(
                _settings.Preferences.Current,
                _settings.FavoriteTabs.Current,
                BuildCandidate(),
                Favorites.Result());
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(IsDirty) or nameof(Note) or nameof(SelectedCategory) or nameof(SelectedCommand)))
        {
            RefreshDraftState();
        }
    }

    public IReadOnlyList<string> Languages => [Labels.LanguageEnglish, Labels.LanguageFrench];

    public string FolderText => _text.Get("settings.folder_current").Replace("{path}", _settings.Directory, StringComparison.Ordinal);

    /// <summary>Raised by OK: the window closes itself.</summary>
    public event Action? CloseRequested;

    public Control CreateContent() => new SettingsView(this);

    /// <summary>What Apply would store: a copy of the live preferences with the window's fields written over it.</summary>
    private Preferences BuildCandidate()
    {
        var next = PreferencesCopy.Of(_settings.Preferences.Current);
        next.Language = LanguageIndex == 1 ? Language.Fr : Language.En;
        next.FontSize = (float)Math.Clamp(FontSize ?? (decimal)Preferences.DefaultFontSize, MinFontSize, MaxFontSize);
        next.ShowHidden = ShowHidden;
        next.UseTrash = UseTrash;
        next.ConfirmDelete = ConfirmDelete;
        next.ConfirmTrashFallback = ConfirmTrashFallback;
        next.LogOperations = LogOperations;
        next.AutoFolderSizes = AutoFolderSizes;
        next.CheckUpdates = CheckUpdates;
        next.IndexRoots = IndexRoots.Trim();
        next.IndexExcludes = IndexExcludes.Trim();
        next.IndexRefreshHours = (int)Math.Clamp(IndexRefreshHours ?? Preferences.DefaultIndexRefreshHours, 0, Preferences.MaxIndexRefreshHours);
        next.PreviewStartSeconds = (int)Math.Clamp(PreviewStartSeconds ?? 0, 0, Preferences.MaxPreviewStartSeconds);
        next.Editor = Editor.Trim();
        next.Terminal = Terminal.Trim();
        next.ColumnSets = [.. ColumnSets.Result()];
        next.Toolbar = [.. Toolbar.Result()];
        next.Associations = [.. Associations.Result()];
        next.FavoriteTabsMode = Favorites.Mode;
        next.UserCommands = Commands.Select(row => row.Command).ToList();
        return next;
    }

    /// <summary>Saves the draft over a copy of the stored preferences and applies the live parts.</summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    public void Apply()
    {
        var next = BuildCandidate();
        var languageChanged = next.Language != _settings.Preferences.Current.Language;
        var favorites = Favorites.Result();
        // Same preferences on both sides, so only the favorite sets can differ. Changed misses an outside save.
        if (PreferencesDraft.Differs(
            _settings.Preferences.Current,
            _settings.FavoriteTabs.Current,
            _settings.Preferences.Current,
            favorites))
        {
            _settings.FavoriteTabs.Save(favorites);
        }

        _settings.Preferences.Save(next);
        _log.Enabled = next.LogOperations;
        RefreshDraftState();
        if (languageChanged)
        {
            _text.Use(next.Language);
            Note = _text.Get("settings.restart_language");
            return;
        }

        Note = _text.Get("settings.saved");
    }

    /// <summary>OK: with nothing changed it only closes; otherwise Apply, then close.</summary>
    [RelayCommand]
    public void Ok()
    {
        RefreshDraftState();
        if (IsDirty)
        {
            Apply();
        }

        CloseRequested?.Invoke();
    }

    /// <summary>Cancel: nothing is saved, the window closes.</summary>
    [RelayCommand]
    public void Cancel() => CloseRequested?.Invoke();

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

    private IEnumerable<string> UserCommandNames() => Commands.Select(row => row.Command.Name);

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
        IndexRoots = current.IndexRoots;
        IndexExcludes = current.IndexExcludes;
        IndexRefreshHours = current.IndexRefreshHours;
        PreviewStartSeconds = current.PreviewStartSeconds;
        Editor = current.Editor;
        Terminal = current.Terminal;
        foreach (var command in current.UserCommands)
        {
            Commands.Add(new UserCommandRow(command));
        }
    }
}

/// <summary>One entry of the settings window category list.</summary>
public sealed record SettingsCategoryItem(string Id, string Name);
