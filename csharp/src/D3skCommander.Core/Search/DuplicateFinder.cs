using System.IO.Hashing;

namespace D3skCommander.Core.Search;

/// <summary>What makes two files duplicates. Content implies size. At least one of the three must be set.</summary>
public sealed record DuplicateCriteria(bool Name, bool Size, bool Content, bool CompareBytes)
{
    public bool IsValid => Name || Size || Content;
}

/// <summary>A file offered to the finder.</summary>
public sealed record DuplicateFile(string Path, ulong Size);

/// <summary>Two or more files that match. <see cref="Number"/> is 1-based, in the order of wasted bytes.</summary>
public sealed record DuplicateGroup(int Number, IReadOnlyList<DuplicateFile> Files)
{
    /// <summary>Bytes freed by keeping one copy.</summary>
    public ulong WastedBytes => Files.Count < 2 ? 0 : Files[0].Size * (ulong)(Files.Count - 1);
}

/// <summary>What <see cref="DuplicateFinder.FindAsync"/> found. <see cref="Skipped"/> counts files that could not be read.</summary>
public sealed record DuplicateResult(IReadOnlyList<DuplicateGroup> Groups, int Skipped);

/// <summary>
/// Groups files that are the same by name, size and/or content. Content: only files whose size collides
/// are read (XxHash128, streamed), and with <see cref="DuplicateCriteria.CompareBytes"/> files of equal hash
/// are compared byte by byte. A file that cannot be read is skipped and counted. Groups of one are dropped,
/// groups are numbered by wasted bytes (biggest first), files of a group are ordered by path.
/// Cancelling throws <see cref="OperationCanceledException"/>; nothing partial is returned.
/// </summary>
public sealed class DuplicateFinder
{
    private const int BufferSize = 81920;

    private readonly Func<string, CancellationToken, Task<string>> _hash;

    public DuplicateFinder()
        : this(HashFileAsync)
    {
    }

    /// <param name="hash">Digest of a file's content as text. Replaceable so a test can fake a collision.</param>
    public DuplicateFinder(Func<string, CancellationToken, Task<string>> hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        _hash = hash;
    }

    /// <param name="progress">Called with (files hashed, files to hash) before the first and after each file.</param>
    public async Task<DuplicateResult> FindAsync(
        IReadOnlyList<DuplicateFile> files,
        DuplicateCriteria criteria,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(criteria);
        if (!criteria.IsValid)
        {
            throw new ArgumentException("At least one criterion is needed.", nameof(criteria));
        }

        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var useSize = criteria.Size || criteria.Content;
        var buckets = files
            .GroupBy(file => (criteria.Name ? Path.GetFileName(file.Path) : "", useSize ? file.Size : 0UL), new KeyComparer(comparer))
            .Select(bucket => bucket.ToList())
            .Where(bucket => bucket.Count > 1)
            .ToList();
        var skipped = 0;
        if (criteria.Content)
        {
            var refined = await RefineAsync(buckets, criteria, progress, cancellationToken).ConfigureAwait(false);
            buckets = refined.Buckets;
            skipped = refined.Skipped;
        }

        return new DuplicateResult(Number(buckets), skipped);
    }

    private static List<DuplicateGroup> Number(List<List<DuplicateFile>> buckets)
    {
        var ordered = buckets
            .Select(bucket => bucket.OrderBy(file => file.Path, StringComparer.Ordinal).ToList())
            .OrderByDescending(bucket => bucket[0].Size * (ulong)(bucket.Count - 1))
            .ThenBy(bucket => bucket[0].Path, StringComparer.Ordinal)
            .ToList();
        var groups = new List<DuplicateGroup>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            groups.Add(new DuplicateGroup(i + 1, ordered[i]));
        }

        return groups;
    }

    private async Task<(List<List<DuplicateFile>> Buckets, int Skipped)> RefineAsync(
        List<List<DuplicateFile>> buckets,
        DuplicateCriteria criteria,
        Action<long, long>? progress,
        CancellationToken token)
    {
        var total = buckets.Where(bucket => bucket[0].Size > 0).Sum(bucket => (long)bucket.Count);
        long done = 0;
        var skipped = 0;
        var result = new List<List<DuplicateFile>>();
        progress?.Invoke(0, total);
        foreach (var bucket in buckets)
        {
            token.ThrowIfCancellationRequested();
            if (bucket[0].Size == 0)
            {
                result.Add(bucket);
                continue;
            }

            var byHash = new Dictionary<string, List<DuplicateFile>>(StringComparer.Ordinal);
            foreach (var file in bucket)
            {
                token.ThrowIfCancellationRequested();
                var digest = await TryHashAsync(file.Path, token).ConfigureAwait(false);
                if (digest is null)
                {
                    skipped++;
                }
                else
                {
                    AddTo(byHash, digest, file);
                }

                progress?.Invoke(++done, total);
            }

            foreach (var same in byHash.Values.Where(list => list.Count > 1))
            {
                var parts = criteria.CompareBytes ? await SplitByBytesAsync(same, token).ConfigureAwait(false) : [same];
                result.AddRange(parts.Where(part => part.Count > 1));
            }
        }

        return (result, skipped);
    }

    private async Task<string?> TryHashAsync(string path, CancellationToken token)
    {
        try
        {
            return await _hash(path, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void AddTo(Dictionary<string, List<DuplicateFile>> map, string key, DuplicateFile file)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(file);
    }

    // Files equal to a class's first member join it; the others start a class. Unreadable files drop out.
    private static async Task<List<List<DuplicateFile>>> SplitByBytesAsync(List<DuplicateFile> same, CancellationToken token)
    {
        var classes = new List<List<DuplicateFile>>();
        foreach (var file in same)
        {
            var placed = false;
            foreach (var candidate in classes)
            {
                if (await SameBytesAsync(candidate[0].Path, file.Path, token).ConfigureAwait(false))
                {
                    candidate.Add(file);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                classes.Add([file]);
            }
        }

        return classes;
    }

    private static async Task<bool> SameBytesAsync(string left, string right, CancellationToken token)
    {
        try
        {
            await using var a = Open(left);
            await using var b = Open(right);
            var bufferA = new byte[BufferSize];
            var bufferB = new byte[BufferSize];
            while (true)
            {
                var readA = await ReadFullAsync(a, bufferA, token).ConfigureAwait(false);
                var readB = await ReadFullAsync(b, bufferB, token).ConfigureAwait(false);
                if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                {
                    return false;
                }

                if (readA == 0)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = Open(path);
        var hash = new XxHash128();
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            hash.Append(buffer.AsSpan(0, read));
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, useAsync: true);

    private sealed class KeyComparer(StringComparer names) : IEqualityComparer<(string Name, ulong Size)>
    {
        public bool Equals((string Name, ulong Size) x, (string Name, ulong Size) y) =>
            x.Size == y.Size && names.Equals(x.Name, y.Name);

        public int GetHashCode((string Name, ulong Size) key) => HashCode.Combine(names.GetHashCode(key.Name), key.Size);
    }
}
