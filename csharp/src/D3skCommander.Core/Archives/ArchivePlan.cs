using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

/// <summary>What to extract, and how to strip the browsed folder from stored paths.</summary>
internal sealed class ArchivePlan
{
    private readonly string[]? _selection;
    private readonly string _base;

    private ArchivePlan(string[]? selection, string commonParent)
    {
        _selection = selection;
        _base = commonParent;
    }

    public static ArchivePlan From(IReadOnlyList<string>? entries)
    {
        if (entries is null)
        {
            return new ArchivePlan(null, "");
        }

        var selected = new string[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            selected[i] = ArchivePaths.Normalize(entries[i] ?? "");
        }

        return new ArchivePlan(selected, CommonParent(selected));
    }

    public bool Wants(string normalized)
    {
        if (_selection is null)
        {
            return true;
        }

        foreach (var item in _selection)
        {
            if (item.Length == 0 || normalized == item || normalized.StartsWith(item + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public Resolved Resolve(string destination, string raw)
    {
        var normalized = ArchivePaths.Normalize(raw);
        if (!Wants(normalized))
        {
            return Resolved.None();
        }

        var reason = ArchivePaths.UnsafeReason(raw);
        if (reason is not null)
        {
            return Resolved.Fail(OperationError.InvalidData($"unsafe path in archive ({reason}): {raw}"));
        }

        var relative = Relative(normalized);
        if (relative.Length == 0)
        {
            return Resolved.None();
        }

        var path = destination;
        foreach (var part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
        }

        return Resolved.At(path, normalized);
    }

    private string Relative(string normalized)
    {
        if (_base.Length == 0)
        {
            return normalized;
        }

        var prefix = _base + "/";
        if (normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            return normalized[prefix.Length..];
        }

        return normalized;
    }

    private static string CommonParent(string[] selected)
    {
        string[]? common = null;
        foreach (var item in selected)
        {
            var comps = ParentsOf(item);
            if (common is null)
            {
                common = comps;
                continue;
            }

            var length = 0;
            while (length < common.Length && length < comps.Length && common[length] == comps[length])
            {
                length++;
            }

            if (length != common.Length)
            {
                var next = new string[length];
                Array.Copy(common, next, length);
                common = next;
            }
        }

        if (common is null || common.Length == 0)
        {
            return "";
        }

        return string.Join('/', common);
    }

    private static string[] ParentsOf(string item)
    {
        var parts = ArchivePaths.Normalize(item).Split('/');
        if (parts.Length == 0 || (parts.Length == 1 && parts[0].Length == 0))
        {
            return [];
        }

        var parents = new string[parts.Length - 1];
        Array.Copy(parts, parents, parents.Length);
        return parents;
    }
}

internal readonly struct Resolved
{
    private Resolved(string? path, string? normalized, OperationError? error)
    {
        Path = path;
        Normalized = normalized;
        Error = error;
    }

    public string? Path { get; }

    public string? Normalized { get; }

    public OperationError? Error { get; }

    public bool IsSkip => Path is null && Error is null;

    public static Resolved None() => new(null, null, null);

    public static Resolved Fail(OperationError error) => new(null, null, error);

    public static Resolved At(string path, string normalized) => new(path, normalized, null);
}
