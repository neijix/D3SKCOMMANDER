namespace D3skCommander.Core.Trash;

/// <summary>The two delete preferences. Defaults match v0.1: trash on, confirm on.</summary>
public sealed record DeleteSettings(bool UseTrash = true, bool ConfirmDelete = true);
