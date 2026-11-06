namespace D3skCommander.Core.Trash;

/// <summary>What the user answers when the trash is unavailable.</summary>
public enum TrashFallbackAnswer
{
    Cancel,
    Delete,
    DeleteAndStopAsking,
}

/// <summary>
/// The choices of the "trash unavailable" prompt, in order, and how a picked index maps back.
/// A closed or cancelled dialog (null) keeps the files.
/// </summary>
public static class TrashFallback
{
    public const string MessageKey = "fileops.trash_failed";

    public static IReadOnlyList<string> ChoiceKeys { get; } = ["fileops.delete_now", "fileops.delete_dont_ask"];

    public static TrashFallbackAnswer Map(int? choice) => choice switch
    {
        0 => TrashFallbackAnswer.Delete,
        1 => TrashFallbackAnswer.DeleteAndStopAsking,
        _ => TrashFallbackAnswer.Cancel,
    };
}
