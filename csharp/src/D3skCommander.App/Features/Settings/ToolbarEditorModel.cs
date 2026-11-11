using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Icons;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Features.Settings;

/// <summary>
/// The "Toolbar" section of the settings window: a draft of the toolbar entries. Add a command, a
/// user command, a program or a separator; remove, move, pick an icon, set a tooltip, reset.
/// </summary>
public sealed partial class ToolbarEditorModel : ObservableObject
{
    private readonly ILocalizer _text;
    private bool _loading;

    [ObservableProperty]
    private ToolbarRow? _selectedRow;

    public ToolbarEditorModel(ILocalizer text, IEnumerable<ToolbarItem> items, IEnumerable<string> userCommandNames)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(userCommandNames);
        _text = text;
        Commands = [.. CommandCatalog.All.Select(id => new CommandChoice(id, text.Get(CommandCatalog.LabelKey(id)))).OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase)];
        Icons = [new IconChoice("", text.Get("toolbar.icon_default"), ToolbarGlyphs.For(ToolbarIcons.Generic)), .. ToolbarIcons.Names.Select(name => new IconChoice(name, text.Get("toolbar.icon." + name), ToolbarGlyphs.For(name)))];
        SetUserCommandNames(userCommandNames);
        Rows.CollectionChanged += OnRowsChanged;
        Load(items);
    }

    /// <summary>Raised when the draft changed: an entry added, removed or moved, the list reset, or a field edited.</summary>
    public event EventHandler? DraftChanged;

    public IReadOnlyList<CommandChoice> Commands { get; }

    public IReadOnlyList<IconChoice> Icons { get; }

    public ObservableCollection<string> UserCommandNames { get; } = [];

    public ObservableCollection<ToolbarRow> Rows { get; } = [];

    public string Title => _text.Get("toolbar.title");

    public string AddCommandCaption => _text.Get("toolbar.add_command");

    public string AddUserCommandCaption => _text.Get("toolbar.add_user");

    public string AddProgramCaption => _text.Get("toolbar.add_program");

    public string AddSeparatorCaption => _text.Get("toolbar.add_separator");

    public string RemoveCaption => _text.Get("toolbar.remove");

    public string UpCaption => _text.Get("toolbar.up");

    public string DownCaption => _text.Get("toolbar.down");

    public string ResetCaption => _text.Get("toolbar.reset");

    public string CommandCaption => _text.Get("toolbar.command");

    public string UserCommandCaption => _text.Get("toolbar.user_command");

    public string ProgramCaption => _text.Get("toolbar.program");

    public string ArgumentsCaption => _text.Get("toolbar.arguments");

    public string FolderCaption => _text.Get("toolbar.folder");

    public string IconCaption => _text.Get("toolbar.icon");

    public string TooltipCaption => _text.Get("toolbar.tooltip");

    public string Hint => _text.Get("toolbar.hint");

    /// <summary>The entries of the draft, cleaned: what Apply stores.</summary>
    public IReadOnlyList<ToolbarItem> Result() => ToolbarItems.Clean(Rows.Select(row => row.ToItem()));

    /// <summary>Keeps the user-command drop-down in step with the list of the settings window.</summary>
    public void SetUserCommandNames(IEnumerable<string> names)
    {
        var next = names.ToList();
        if (!UserCommandNames.SequenceEqual(next))
        {
            UserCommandNames.Clear();
            next.ForEach(UserCommandNames.Add);
        }
    }

    /// <summary>Adds a button for <paramref name="command"/> after the selected entry (else at the end) and selects it.</summary>
    public ToolbarRow AddCommand(CommandId command) => Insert(Row(ToolbarItem.ForCommand(command)));

    public ToolbarRow AddUserCommand(string name) => Insert(Row(ToolbarItem.ForUserCommand(name)));

    public ToolbarRow AddProgram(string program, string arguments = "", string workingFolder = "") =>
        Insert(Row(ToolbarItem.ForProgram(program, arguments, workingFolder)));

    public ToolbarRow AddSeparator() => Insert(Row(ToolbarItem.Separator));

    [RelayCommand]
    public void AddCommandButton()
    {
        var used = Rows.Where(row => row.Command is not null).Select(row => row.Command!.Id).ToHashSet();
        AddCommand((Commands.FirstOrDefault(choice => !used.Contains(choice.Id)) ?? Commands[0]).Id);
    }

    [RelayCommand]
    public void AddUserCommandButton() => AddUserCommand(UserCommandNames.FirstOrDefault() ?? "");

    [RelayCommand]
    public void AddProgramButton() => AddProgram("");

    [RelayCommand]
    public void AddSeparatorButton() => AddSeparator();

    [RelayCommand]
    public void Remove()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var next = ToolbarItems.Remove(Rows, Rows.IndexOf(SelectedRow));
        SelectedRow = next < 0 ? null : Rows[next];
    }

    [RelayCommand]
    public void MoveUp() => Move(-1);

    [RelayCommand]
    public void MoveDown() => Move(1);

    [RelayCommand]
    public void Reset()
    {
        Load(ToolbarDefaults.Items());
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Move(int delta)
    {
        var row = SelectedRow;
        if (row is not null)
        {
            ToolbarItems.Move(Rows, Rows.IndexOf(row), delta);
            SelectedRow = row;
        }
    }

    private void OnRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var added in e.NewItems?.OfType<ToolbarRow>() ?? [])
        {
            added.PropertyChanged += OnRowPropertyChanged;
        }

        foreach (var removed in e.OldItems?.OfType<ToolbarRow>() ?? [])
        {
            removed.PropertyChanged -= OnRowPropertyChanged;
        }

        if (!_loading)
        {
            DraftChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Display and Glyph follow the fields: only the fields count as an edit.
    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ToolbarRow.Display) or nameof(ToolbarRow.Glyph)))
        {
            DraftChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Load(IEnumerable<ToolbarItem> items)
    {
        _loading = true;
        try
        {
            Rows.Clear();
            foreach (var item in ToolbarItems.Clean(items))
            {
                Rows.Add(Row(item));
            }
        }
        finally
        {
            _loading = false;
        }

        SelectedRow = Rows.FirstOrDefault();
    }

    private ToolbarRow Insert(ToolbarRow row)
    {
        var at = SelectedRow is null ? Rows.Count : Rows.IndexOf(SelectedRow) + 1;
        Rows.Insert(at, row);
        SelectedRow = row;
        return row;
    }

    private ToolbarRow Row(ToolbarItem item) => new(item.Kind, _text)
    {
        Command = item.Command is CommandId id ? Commands.First(choice => choice.Id == id) : null,
        UserCommandName = item.UserCommandName,
        Program = item.Program,
        Arguments = item.Arguments,
        WorkingFolder = item.WorkingFolder,
        Icon = Icons.FirstOrDefault(choice => choice.Name == item.Icon) ?? Icons[0],
        Tooltip = item.Tooltip,
    };
}
