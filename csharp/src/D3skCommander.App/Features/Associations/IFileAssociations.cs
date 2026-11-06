using D3skCommander.App.Shell;
using D3skCommander.Core.Associations;

namespace D3skCommander.App.Features.Associations;

/// <summary>The file associations of the preferences, as the panels, the viewer and the editor use them.</summary>
public interface IFileAssociations
{
    /// <summary>Whether a rule defines <paramref name="kind"/> for the file.</summary>
    bool Has(AssociationKind kind, string path);

    /// <summary>
    /// Starts the program of the first rule that defines <paramref name="kind"/> for the file, off the UI
    /// thread. Null when no rule does: the caller keeps its built-in behavior. The task ends once the
    /// program started; a failure was shown to the user.
    /// </summary>
    Task? TryStart(AssociationKind kind, string path);

    /// <summary>The context-menu entries for the file: Open, View and Edit with the rule's program, then its extra actions.</summary>
    IReadOnlyList<ShellAction> MenuFor(string path);
}
