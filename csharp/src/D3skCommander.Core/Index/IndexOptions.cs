namespace D3skCommander.Core.Index;

/// <summary>What the name index covers: the roots to walk (empty: every mounted volume) and the folders to leave out.</summary>
public sealed record IndexOptions(IReadOnlyList<string> Roots, IReadOnlyList<string> Excludes)
{
    /// <summary>Pseudo and system folders, caches and tool folders nobody searches for files in.</summary>
    public const string DefaultExcludes = "/proc;/sys;/dev;/run;/snap;/var/lib;*/.cache;*/node_modules;*/.git;*/.Trash-*";

    /// <summary>Roots and excludes as the settings window keeps them: ';'-separated texts.</summary>
    public static IndexOptions From(string? roots, string? excludes) => new(Split(roots), Split(excludes));

    /// <summary>The non-empty, trimmed entries of a ';'-separated text.</summary>
    public static IReadOnlyList<string> Split(string? text) =>
        (text ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
