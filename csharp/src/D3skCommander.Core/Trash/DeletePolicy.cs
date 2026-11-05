using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Trash;

/// <summary>
/// Delete rules (A6). Trash or permanent follows the settings, unless the command is
/// DeletePermanent (Shift+F8 / Shift+Del), which is always permanent. Confirmation follows
/// the settings. A missing OS trash is <see cref="OfferPermanent"/>.
/// </summary>
public static class DeletePolicy
{
    public const string TrashPromptKey = "dlg.delete";

    public const string PermanentPromptKey = "dlg.delete_perm";

    public static DeleteDecision Decide(DeleteSettings settings, CommandId command)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (command is not (CommandId.Delete or CommandId.DeletePermanent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command,
                "Delete policy only decides Delete and DeletePermanent.");
        }

        var trash = settings.UseTrash && command != CommandId.DeletePermanent;
        return new DeleteDecision(
            trash ? DeleteMode.Trash : DeleteMode.Permanent,
            settings.ConfirmDelete,
            trash ? TrashPromptKey : PermanentPromptKey);
    }

    /// <summary>
    /// The OS has no trash. The dialog offers a permanent delete and always asks,
    /// so a failed trash does not become a silent permanent delete when confirm is off.
    /// </summary>
    public static DeleteDecision OfferPermanent(DeleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new DeleteDecision(DeleteMode.Permanent, true, PermanentPromptKey);
    }
}
