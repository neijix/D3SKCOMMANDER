using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

/// <summary>List, extract and pack ZIP, TAR, TAR.GZ and 7Z. Unsafe member names are refused.</summary>
public static class ArchiveIo
{
    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List(string archive)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        var kind = ArchiveKinds.FromPath(archive);
        if (kind is null)
        {
            return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Fail(Unknown(archive));
        }

        if (kind == ArchiveKind.Zip)
        {
            return ArchiveZip.List(archive);
        }

        if (kind == ArchiveKind.Tar)
        {
            return ArchiveTar.List(archive, false);
        }

        if (kind == ArchiveKind.TarGz)
        {
            return ArchiveTar.List(archive, true);
        }

        if (kind == ArchiveKind.SevenZ)
        {
            return ArchiveSevenZip.List(archive);
        }

        return ArchiveExternal.List(archive);
    }

    /// <summary>
    /// Extracts <paramref name="entries"/> (<c>null</c> = everything; a folder takes its subtree) into
    /// <paramref name="destination"/>. Returning false from <paramref name="progress"/> stops; files
    /// already written stay. A cancelled token removes nothing already written and throws.
    /// </summary>
    public static ArchiveResult<int> Extract(
        string archive,
        IReadOnlyList<string>? entries,
        string destination,
        Func<string, bool>? progress = null) =>
        Extract(archive, entries, destination, progress, CancellationToken.None);

    public static ArchiveResult<int> Extract(
        string archive,
        IReadOnlyList<string>? entries,
        string destination,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        var kind = ArchiveKinds.FromPath(archive);
        if (kind is null)
        {
            return ArchiveResult<int>.Fail(Unknown(archive));
        }

        if (kind == ArchiveKind.External)
        {
            return ArchiveExternal.Extract(archive, entries, destination, progress, cancellationToken);
        }

        try
        {
            Directory.CreateDirectory(destination);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ArchiveResult<int>.Fail(ArchiveErrors.From(ex, destination));
        }

        var plan = ArchivePlan.From(entries);
        if (kind == ArchiveKind.Zip)
        {
            return ArchiveZip.Extract(archive, plan, destination, progress, cancellationToken);
        }

        if (kind == ArchiveKind.Tar)
        {
            return ArchiveTar.Extract(archive, false, plan, destination, progress, cancellationToken);
        }

        if (kind == ArchiveKind.TarGz)
        {
            return ArchiveTar.Extract(archive, true, plan, destination, progress, cancellationToken);
        }

        return ArchiveSevenZip.Extract(archive, plan, destination, progress, cancellationToken);
    }

    /// <summary>
    /// Packs <paramref name="sources"/> into <paramref name="archive"/>. <paramref name="level"/> is 0
    /// (store, or the fastest 7z) to 9. Returning false from <paramref name="progress"/> cancels:
    /// the partial archive is removed and the error is <see cref="ErrorKind.Cancelled"/>.
    /// </summary>
    public static ArchiveResult<int> Create(
        string archive,
        ArchiveKind kind,
        IReadOnlyList<string> sources,
        int level,
        Func<string, bool>? progress = null) =>
        Create(archive, kind, sources, level, progress, CancellationToken.None);

    public static ArchiveResult<int> Create(
        string archive,
        ArchiveKind kind,
        IReadOnlyList<string> sources,
        int level,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentNullException.ThrowIfNull(sources);
        if (kind == ArchiveKind.External)
        {
            return ArchiveResult<int>.Fail(OperationError.Unsupported("this format cannot be created"));
        }

        var part = archive + ".part";
        var collected = ArchiveSources.Collect(sources, archive, part);
        if (collected.Error is not null)
        {
            return ArchiveResult<int>.Fail(collected.Error);
        }

        ArchiveResult<int> written;
        try
        {
            written = Write(part, kind, collected.Value, level, progress, cancellationToken);
        }
        catch
        {
            ArchiveErrors.Delete(part);
            throw;
        }

        if (written.Error is not null)
        {
            ArchiveErrors.Delete(part);
            return written;
        }

        try
        {
            File.Move(part, archive, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ArchiveErrors.Delete(part);
            return ArchiveResult<int>.Fail(ArchiveErrors.From(ex, archive));
        }

        return written;
    }

    /// <summary>Direct children of <paramref name="inner"/> (<c>""</c> is the root).</summary>
    public static IReadOnlyList<ArchiveEntry> ListDirectory(IReadOnlyList<ArchiveEntry> entries, string inner)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(inner);
        var folder = inner.Trim('/');
        var found = new List<ArchiveEntry>();
        foreach (var entry in entries)
        {
            if (IsDirectChild(entry.Path, folder))
            {
                found.Add(entry);
            }
        }

        return found;
    }

    private static ArchiveResult<int> Write(
        string part,
        ArchiveKind kind,
        List<SourceItem> items,
        int level,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        if (kind == ArchiveKind.Zip)
        {
            return ArchiveZip.Create(part, items, level, progress, cancellationToken);
        }

        if (kind == ArchiveKind.Tar)
        {
            return ArchiveTar.Create(part, false, items, level, progress, cancellationToken);
        }

        if (kind == ArchiveKind.TarGz)
        {
            return ArchiveTar.Create(part, true, items, level, progress, cancellationToken);
        }

        if (kind == ArchiveKind.SevenZ)
        {
            return ArchiveSevenZip.Create(part, items, level, progress, cancellationToken);
        }

        return ArchiveResult<int>.Fail(OperationError.Unsupported("this format cannot be created"));
    }

    private static bool IsDirectChild(string path, string folder)
    {
        string rest;
        if (folder.Length == 0)
        {
            rest = path;
        }
        else if (path.StartsWith(folder + "/", StringComparison.Ordinal))
        {
            rest = path[(folder.Length + 1)..];
        }
        else
        {
            return false;
        }

        return rest.Length > 0 && !rest.Contains('/');
    }

    private static OperationError Unknown(string archive) =>
        OperationError.Unsupported($"unknown archive type: {archive}");
}
