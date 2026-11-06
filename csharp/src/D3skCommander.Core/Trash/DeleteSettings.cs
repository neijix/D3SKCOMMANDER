namespace D3skCommander.Core.Trash;

/// <summary>
/// The delete preferences. Defaults match v0.1: trash on, confirm on. <see cref="ConfirmTrashFallback"/>
/// asks before a permanent delete when the trash is unavailable; the user turns it off with
/// "Delete, don't ask again".
/// </summary>
public sealed record DeleteSettings(bool UseTrash = true, bool ConfirmDelete = true, bool ConfirmTrashFallback = true);
