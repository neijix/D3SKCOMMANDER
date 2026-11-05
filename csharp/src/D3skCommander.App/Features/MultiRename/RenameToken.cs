namespace D3skCommander.App.Features.MultiRename;

/// <summary>One mask token the multi-rename form inserts. The label is the token in both languages.</summary>
public sealed class RenameToken
{
    private RenameToken(string labelKey, string hintKey)
    {
        LabelKey = labelKey;
        HintKey = hintKey;
    }

    public string LabelKey { get; }

    public string HintKey { get; }

    public static IReadOnlyList<RenameToken> All { get; } =
    [
        new("rename.tok.n", "rename.hint.n"),
        new("rename.tok.e", "rename.hint.e"),
        new("rename.tok.n13", "rename.hint.n13"),
        new("rename.tok.n2", "rename.hint.n2"),
        new("rename.tok.e12", "rename.hint.e12"),
        new("rename.tok.c", "rename.hint.c"),
        new("rename.tok.c113", "rename.hint.c113"),
        new("rename.tok.p", "rename.hint.p"),
        new("rename.tok.g", "rename.hint.g"),
        new("rename.tok.ymd", "rename.hint.ymd"),
        new("rename.tok.hms", "rename.hint.hms"),
        new("rename.tok.y", "rename.hint.y"),
        new("rename.tok.mo", "rename.hint.mo"),
        new("rename.tok.d", "rename.hint.d"),
        new("rename.tok.h", "rename.hint.h"),
        new("rename.tok.mi", "rename.hint.mi"),
        new("rename.tok.s", "rename.hint.s"),
    ];
}
