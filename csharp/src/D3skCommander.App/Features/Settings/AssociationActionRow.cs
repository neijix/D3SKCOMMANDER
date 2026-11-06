using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.Associations;

namespace D3skCommander.App.Features.Settings;

/// <summary>One action being edited: the program, its arguments and working folder, and (an extra action) its menu name.</summary>
public sealed partial class AssociationActionRow : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _program = "";

    [ObservableProperty]
    private string _arguments = "";

    [ObservableProperty]
    private string _workingFolder = "";

    public static AssociationActionRow From(AssociationAction? action) => new()
    {
        Name = action?.Name ?? "",
        Program = action?.Program ?? "",
        Arguments = action?.Arguments ?? "",
        WorkingFolder = action?.WorkingFolder ?? "",
    };

    /// <summary>The action, or null while no program is typed.</summary>
    public AssociationAction? ToAction() => Program.Trim().Length == 0
        ? null
        : new AssociationAction(Program.Trim(), Arguments.Trim(), WorkingFolder.Trim(), Name.Trim());
}
