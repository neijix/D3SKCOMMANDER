using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Index;

/// <summary>Which kind of entry a query wants.</summary>
public enum NameKind
{
    Any,
    File,
    Folder,
}

/// <summary>The cheap forms of a wildcard: one literal with '*' on one or both ends, matched without the mask.</summary>
public enum NameShape
{
    Mask,
    Prefix,
    Suffix,
    Inside,
}

/// <summary>One word of a query: a substring of the name, or a wildcard mask when it has '*' or '?'.</summary>
public sealed record NameTerm(string Text, Mask? Mask)
{
    /// <summary>How a wildcard is matched; <see cref="NameShape.Mask"/> when it is not one of the cheap forms.</summary>
    public NameShape Shape { get; } = ShapeOf(Text, Mask);

    /// <summary>The text of a cheap-form wildcard without its '*'.</summary>
    public string Literal { get; } = Text.Trim('*');

    private static NameShape ShapeOf(string text, Mask? mask)
    {
        var literal = text.Trim('*');
        if (mask is null || literal.Length == 0 || literal.AsSpan().IndexOfAny('*', '?') >= 0)
        {
            return NameShape.Mask;
        }

        var start = text[0] == '*';
        var end = text[^1] == '*';
        return start && end ? NameShape.Inside : start ? NameShape.Suffix : end ? NameShape.Prefix : NameShape.Mask;
    }
}

/// <summary>An inclusive size range in bytes.</summary>
public readonly record struct SizeRange(long Min, long Max)
{
    public static SizeRange Any { get; } = new(0, long.MaxValue);

    public bool Contains(long size) => size >= Min && size <= Max;

    public SizeRange Intersect(SizeRange other) => new(Math.Max(Min, other.Min), Math.Min(Max, other.Max));
}

/// <summary>A parsed query: every part must hold (terms and filters are ANDed). See <see cref="NameQueryParser"/>.</summary>
public sealed class NameQuery
{
    public NameQuery(
        IReadOnlyList<NameTerm> terms,
        IReadOnlyList<string> extensions,
        SizeRange? size,
        IReadOnlyList<string> folderParts,
        NameKind kind,
        IReadOnlyList<string> ignored)
    {
        Terms = terms;
        Extensions = extensions;
        Size = size;
        FolderParts = folderParts;
        Kind = kind;
        Ignored = ignored;
    }

    public IReadOnlyList<NameTerm> Terms { get; }

    /// <summary>Lower-case extensions without the dot; a folder never matches them.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>The size range; a folder never matches it.</summary>
    public SizeRange? Size { get; }

    /// <summary>Texts the parent path must all contain.</summary>
    public IReadOnlyList<string> FolderParts { get; }

    public NameKind Kind { get; }

    /// <summary>Filter words that could not be read (a size of "1x"); they are left out of the match.</summary>
    public IReadOnlyList<string> Ignored { get; }

    /// <summary>True when nothing would be matched against: no term and no filter.</summary>
    public bool IsEmpty => Terms.Count == 0 && Extensions.Count == 0 && Size is null && FolderParts.Count == 0 && Kind == NameKind.Any;

    /// <summary>The first plain word: names equal to it rank first, then names that start with it.</summary>
    public string? RankText => Terms.FirstOrDefault(static term => term.Mask is null)?.Text;
}
