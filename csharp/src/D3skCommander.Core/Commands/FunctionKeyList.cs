namespace D3skCommander.Core.Commands;

/// <summary>v0.1 function-key bar: F3 View, F4 Edit, F5 Copy, F6 Move, F7 MkDir, F8 Delete, Alt+F7 Search, Alt+F4 Quit.</summary>
internal static class FunctionKeyList
{
    public static IReadOnlyList<FunctionKey> All { get; } =
    [
        new(CommandId.View, "fk.view"),
        new(CommandId.Edit, "fk.edit"),
        new(CommandId.Copy, "fk.copy"),
        new(CommandId.Move, "fk.move"),
        new(CommandId.MkDir, "fk.mkdir"),
        new(CommandId.Delete, "fk.delete"),
        new(CommandId.Search, "fk.search"),
        new(CommandId.Quit, "fk.quit"),
    ];
}
