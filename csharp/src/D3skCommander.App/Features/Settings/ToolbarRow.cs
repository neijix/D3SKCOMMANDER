using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Icons;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Features.Settings;

/// <summary>One toolbar entry being edited: the fields of a <see cref="ToolbarItem"/>, with the text the list and the preview show.</summary>
public sealed partial class ToolbarRow : ObservableObject
{
    private readonly ILocalizer _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display), nameof(Glyph))]
    private CommandChoice? _command;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string? _userCommandName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _program = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _arguments = "";

    [ObservableProperty]
    private string _workingFolder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    private IconChoice? _icon;

    [ObservableProperty]
    private string _tooltip = "";

    public ToolbarRow(ToolbarItemKind kind, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Kind = kind;
        _text = text;
    }

    public ToolbarItemKind Kind { get; }

    public bool IsCommand => Kind == ToolbarItemKind.Command;

    public bool IsUserCommand => Kind == ToolbarItemKind.UserCommand;

    public bool IsProgram => Kind == ToolbarItemKind.Program;

    public bool IsSeparator => Kind == ToolbarItemKind.Separator;

    /// <summary>The line of the list: the command, the user command, the program with its arguments, or a rule.</summary>
    public string Display => Kind switch
    {
        ToolbarItemKind.Separator => "— " + _text.Get("toolbar.separator") + " —",
        ToolbarItemKind.Command => Command?.Label ?? _text.Get("toolbar.command"),
        ToolbarItemKind.UserCommand => _text.Get("toolbar.user_command") + ": " + UserCommandName,
        _ => Program.Length == 0 ? _text.Get("toolbar.program") : (Program + " " + Arguments).Trim(),
    };

    /// <summary>The drawing of the picked icon, else of the default for what the entry runs.</summary>
    public Geometry Glyph => ToolbarGlyphs.For(ToItem().EffectiveIcon());

    public ToolbarItem ToItem() => Kind switch
    {
        ToolbarItemKind.Separator => ToolbarItem.Separator,
        ToolbarItemKind.Command => new ToolbarItem(Kind, Command?.Id, Icon: Icon?.Name ?? "", Tooltip: Tooltip),
        ToolbarItemKind.UserCommand => new ToolbarItem(Kind, UserCommandName: UserCommandName ?? "", Icon: Icon?.Name ?? "", Tooltip: Tooltip),
        _ => new ToolbarItem(Kind, Program: Program, Arguments: Arguments, WorkingFolder: WorkingFolder, Icon: Icon?.Name ?? "", Tooltip: Tooltip),
    };
}
