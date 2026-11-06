namespace D3skCommander.Core.Index;

public sealed partial class NameIndex
{
    /// <summary>How many hits a query returns by default.</summary>
    public const int DefaultLimit = 1000;

    private const int ChunkSize = 1 << 16;
    private const int IdBits = 31;
    private const int PathBits = 16;

    /// <summary>
    /// The best <paramref name="limit"/> entries for the query and the number of matches in all. Names equal to the
    /// first plain word come first, then names starting with it, then the rest; shorter paths first inside each group.
    /// The name pool is scanned in parallel chunks, each keeping only its best keys.
    /// </summary>
    public IndexSearchResult Search(NameQuery query, int limit = DefaultLimit, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (query.IsEmpty || Count == 0)
        {
            return IndexSearchResult.None;
        }

        var folderOk = query.FolderParts.Count > 0 ? new sbyte[Count] : null;
        var chunks = (Count + ChunkSize - 1) / ChunkSize;
        var tops = new TopKeys[chunks];
        var totals = new int[chunks];
        var options = new ParallelOptions { CancellationToken = cancel };
        Parallel.For(0, chunks, options, chunk =>
        {
            var top = new TopKeys(limit);
            totals[chunk] = Scan(query, folderOk, chunk * ChunkSize, Math.Min(Count, (chunk + 1) * ChunkSize), top);
            tops[chunk] = top;
        });

        var keys = new List<long>();
        foreach (var top in tops)
        {
            keys.AddRange(top.Keys.ToArray());
        }

        keys.Sort();
        var hits = new List<IndexHit>(Math.Min(limit, keys.Count));
        for (var i = 0; i < keys.Count && i < limit; i++)
        {
            hits.Add(HitAt((int)(keys[i] & ((1L << IdBits) - 1))));
        }

        return new IndexSearchResult(hits, totals.Sum());
    }

    private int Scan(NameQuery query, sbyte[]? folderOk, int from, int to, TopKeys top)
    {
        var rank = query.RankText;
        var total = 0;
        for (var i = from; i < to; i++)
        {
            if (Matches(i, query, folderOk))
            {
                total++;
                top.Add(KeyOf(i, rank));
            }
        }

        return total;
    }

    private bool Matches(int id, NameQuery query, sbyte[]? folderOk)
    {
        var folder = (_flags[id] & FolderFlag) != 0;
        if ((query.Kind == NameKind.File && folder) || (query.Kind == NameKind.Folder && !folder))
        {
            return false;
        }

        if (query.Size is { } range && (folder || !range.Contains(_size[id])))
        {
            return false;
        }

        var name = NameAt(id);
        foreach (var term in query.Terms)
        {
            if (term.Mask is null && name.IndexOf(term.Text, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        if (query.Extensions.Count > 0 && (folder || !HasExtension(name, query.Extensions)))
        {
            return false;
        }

        if (folderOk is not null && !ParentContains(id, folderOk, query.FolderParts))
        {
            return false;
        }

        foreach (var term in query.Terms)
        {
            if (term.Mask is not null && !WildcardMatches(term, name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExtension(ReadOnlySpan<char> name, IReadOnlyList<string> extensions)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        var extension = name[(dot + 1)..];
        foreach (var wanted in extensions)
        {
            if (extension.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private long KeyOf(int id, string? rank)
    {
        long group = 2;
        if (rank is not null)
        {
            var name = NameAt(id);
            group = name.Equals(rank, StringComparison.OrdinalIgnoreCase) ? 0
                : name.StartsWith(rank, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        }

        var length = Math.Min(_pathLength[id], (1 << PathBits) - 1);
        return (group << (IdBits + PathBits)) | ((long)length << IdBits) | (uint)id;
    }

    /// <summary>
    /// A folder's answer to "does my path contain every text?", worked out the first time an entry that passed the
    /// cheap tests asks and kept per folder: 0 unknown, 1 yes, -1 no. Threads may race on a byte; both write the same answer.
    /// </summary>
    private bool ParentContains(int id, sbyte[] memo, IReadOnlyList<string> parts)
    {
        var parent = _parent[id];
        if (parent < 0)
        {
            return ContainsAll(FolderOf(id), parts);
        }

        if (memo[parent] == 0)
        {
            memo[parent] = ContainsAll(PathOf(parent), parts) ? (sbyte)1 : (sbyte)-1;
        }

        return memo[parent] > 0;
    }

    private static bool ContainsAll(string path, IReadOnlyList<string> parts)
    {
        foreach (var part in parts)
        {
            if (!path.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private IndexHit HitAt(int id) =>
        new(id, NameOf(id), FolderOf(id), PathOf(id), IsFolder(id), _size[id], ModifiedOf(id));

    private static string JoinPath(string parent, ReadOnlySpan<char> name)
    {
        if (parent.Length == 0 || parent[^1] == Path.DirectorySeparatorChar || parent[^1] == Path.AltDirectorySeparatorChar)
        {
            return string.Concat(parent, name);
        }

        return string.Concat(parent, Path.DirectorySeparatorChar.ToString(), name);
    }

    private static bool WildcardMatches(NameTerm term, ReadOnlySpan<char> name) => term.Shape switch
    {
        NameShape.Prefix => name.StartsWith(term.Literal, StringComparison.OrdinalIgnoreCase),
        NameShape.Suffix => name.EndsWith(term.Literal, StringComparison.OrdinalIgnoreCase),
        NameShape.Inside => name.IndexOf(term.Literal, StringComparison.OrdinalIgnoreCase) >= 0,
        _ => term.Mask!.Matches(new string(name)),
    };
}
