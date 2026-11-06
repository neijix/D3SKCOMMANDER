namespace D3skCommander.Core.Associations;

/// <summary>Which of the three built-in gestures an action answers: Enter or double-click, F3, F4.</summary>
public enum AssociationKind
{
    Open,
    View,
    Edit,
}

/// <summary>
/// A program to start for a file: the <see cref="Program"/> as typed (it may hold spaces), its
/// <see cref="Arguments"/> with the placeholders of user commands (<c>%s</c> <c>%p</c> <c>%f</c> <c>%d</c>)
/// and a <see cref="WorkingFolder"/> (empty: the folder of the file). <see cref="Name"/> is set on the
/// extra actions of a rule only; it is the text of the context-menu entry.
/// </summary>
public sealed record AssociationAction(string Program, string Arguments = "", string WorkingFolder = "", string Name = "")
{
    /// <summary>The program without its folder and extension: what "Open with ..." shows.</summary>
    public string ProgramName
    {
        get
        {
            var name = Path.GetFileNameWithoutExtension(Program.Trim());
            return name.Length > 0 ? name : Program.Trim();
        }
    }
}
