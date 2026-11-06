using System.Globalization;
using System.Text.RegularExpressions;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.FileOps;

/// <summary>What a paste does: sources copied or moved into the folder, and same-folder copies with a new name.</summary>
public sealed record PastePlan(bool Move, IReadOnlyList<string> Into, IReadOnlyList<PasteCopy> Duplicates)
{
    public bool IsEmpty => Into.Count == 0 && Duplicates.Count == 0;
}

/// <summary>A source pasted into its own folder, copied to <paramref name="Target"/>.</summary>
public sealed record PasteCopy(string Source, string Target);

/// <summary>
/// The panels' file clipboard: paths copied or cut with Ctrl+C / Ctrl+X, pasted with Ctrl+V into
/// the active folder. A copy pasted into its own folder gets the next free numbered name
/// (<c>a.txt</c> → <c>a (2).txt</c>); a cut pasted into its own folder does nothing.
/// </summary>
public sealed partial class FileClipboard
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public IReadOnlyList<string> Paths { get; private set; } = [];

    public bool IsCut { get; private set; }

    public bool IsEmpty => Paths.Count == 0;

    public void Set(IReadOnlyList<string> paths, bool cut)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Paths = [.. paths];
        IsCut = cut && paths.Count > 0;
    }

    /// <summary>Forgets cut paths once they were moved (a copy stays pasteable).</summary>
    public void Pasted()
    {
        if (IsCut)
        {
            Set([], false);
        }
    }

    /// <summary>The paste into <paramref name="directory"/>; <paramref name="exists"/> tells which names are taken.</summary>
    public PastePlan Plan(string directory, Func<string, bool> exists, Func<string, bool> isDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(isDirectory);
        var into = new List<string>();
        var duplicates = new List<PasteCopy>();
        var claimed = new HashSet<string>(PathComparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var source in Paths)
        {
            if (!SameFolder(source, directory))
            {
                into.Add(source);
            }
            else if (!IsCut)
            {
                var name = NextFreeName(Path.GetFileName(Path.TrimEndingDirectorySeparator(source)), isDirectory(source),
                    candidate => claimed.Contains(candidate) || exists(Path.Combine(directory, candidate)));
                claimed.Add(name);
                duplicates.Add(new PasteCopy(source, Path.Combine(directory, name)));
            }
        }

        return new PastePlan(IsCut, into, duplicates);
    }

    /// <summary>
    /// <c>a.txt</c> → <c>a (2).txt</c>, <c>a (2).txt</c> → <c>a (3).txt</c>, first free. A folder or a
    /// dot file (<c>.env</c>) keeps its whole name as the stem.
    /// </summary>
    public static string NextFreeName(string name, bool isDirectory, Func<string, bool> taken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(taken);
        var parts = isDirectory ? new ExtensionParts(name, "") : LocalFileSystem.SplitExtension(name);
        var stem = parts.Stem.Length == 0 ? name : parts.Stem;
        var extension = parts.Stem.Length == 0 || parts.Extension.Length == 0 ? "" : "." + parts.Extension;
        var number = 2;
        var match = Numbered().Match(stem);
        if (match.Success && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < int.MaxValue)
        {
            stem = match.Groups[1].Value;
            number = n + 1;
        }

        while (true)
        {
            var candidate = stem + " (" + number.ToString(CultureInfo.InvariantCulture) + ")" + extension;
            if (!taken(candidate))
            {
                return candidate;
            }

            number++;
        }
    }

    private static bool SameFolder(string source, string directory)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source));
        return parent is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
            PathComparison);
    }

    [GeneratedRegex(@"^(.*) \((\d+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex Numbered();
}
