namespace D3skCommander.Core.Trash;

/// <summary>Where a delete goes. Trash is the OS trash; permanent removes the path.</summary>
public enum DeleteMode
{
    Trash,
    Permanent,
}

/// <summary>
/// What Core tells the delete dialog. <see cref="PromptKey"/> is <c>dlg.delete</c> for trash
/// and <c>dlg.delete_perm</c> for a permanent delete.
/// </summary>
public sealed record DeleteDecision(DeleteMode Mode, bool NeedsConfirmation, string PromptKey)
{
    public bool GoesToTrash => Mode == DeleteMode.Trash;
}
