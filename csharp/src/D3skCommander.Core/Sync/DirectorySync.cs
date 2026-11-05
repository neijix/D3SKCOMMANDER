using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Sync;

/// <summary>
/// Compare two directory trees. The result is sorted by relative path.
/// Symmetric: left-only and left-newer copy right, right-only and right-newer copy left,
/// different does nothing. Asymmetric: the right side mirrors the left.
/// Directories appear only when empty folders are listed, they are empty, and the other side lacks them.
/// A file facing a directory of the same name is listed as the file's side only.
/// </summary>
public static partial class DirectorySync
{
    public static SyncAction DefaultAction(SyncStatus status, bool asymmetric) =>
        SyncRules.DefaultAction(status, asymmetric);

    public static FileResult<IReadOnlyList<SyncItem>> Compare(
        string left,
        string right,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(left);
        ArgumentException.ThrowIfNullOrEmpty(right);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Mask);

        var mask = new Mask(options.Mask);
        var leftEntries = SyncScan.Read(left, options, mask, cancellationToken);
        if (!leftEntries.IsSuccess)
        {
            return Fail(leftEntries.Error!);
        }

        var rightEntries = SyncScan.Read(right, options, mask, cancellationToken);
        if (!rightEntries.IsSuccess)
        {
            return Fail(rightEntries.Error!);
        }

        return Merge(left, right, leftEntries.Value, rightEntries.Value, options, cancellationToken);
    }

    private static FileResult<IReadOnlyList<SyncItem>> Merge(
        string leftRoot,
        string rightRoot,
        Dictionary<string, SyncEntry> left,
        Dictionary<string, SyncEntry> right,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var items = new List<SyncItem>();
        foreach (var rel in Keys(left, right))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Fail(OperationError.Cancelled(rel));
            }

            var built = Build(rel, leftRoot, rightRoot, left, right, options);
            if (built.Error is not null)
            {
                return Fail(built.Error);
            }

            if (built.Item is not null)
            {
                items.Add(built.Item);
            }
        }

        return FileResult<IReadOnlyList<SyncItem>>.Ok(items);
    }

    private static List<string> Keys(Dictionary<string, SyncEntry> left, Dictionary<string, SyncEntry> right)
    {
        var keys = new List<string>(left.Count + right.Count);
        foreach (var key in left.Keys)
        {
            keys.Add(key);
        }

        foreach (var key in right.Keys)
        {
            if (!left.ContainsKey(key))
            {
                keys.Add(key);
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private static Built Build(
        string rel,
        string leftRoot,
        string rightRoot,
        Dictionary<string, SyncEntry> left,
        Dictionary<string, SyncEntry> right,
        SyncOptions options)
    {
        left.TryGetValue(rel, out var leftEntry);
        right.TryGetValue(rel, out var rightEntry);
        ResolveClash(ref leftEntry, ref rightEntry);
        if (SkipPair(leftEntry, rightEntry))
        {
            return default;
        }

        if (leftEntry is not null && rightEntry is not null)
        {
            return BothFiles(rel, leftRoot, rightRoot, leftEntry, rightEntry, options);
        }

        if (leftEntry is not null)
        {
            return OneSide(rel, SyncStatus.LeftOnly, leftEntry, leftSide: true, options.Asymmetric);
        }

        if (rightEntry is not null)
        {
            return OneSide(rel, SyncStatus.RightOnly, rightEntry, leftSide: false, options.Asymmetric);
        }

        return default;
    }

    private static bool SkipPair(SyncEntry? left, SyncEntry? right)
    {
        if (left is not null && right is not null && left.IsDir && right.IsDir)
        {
            return true;
        }

        if (left is not null && right is null && left.IsDir && !left.Empty)
        {
            return true;
        }

        return right is not null && left is null && right.IsDir && !right.Empty;
    }

    private static void ResolveClash(ref SyncEntry? left, ref SyncEntry? right)
    {
        if (left is null || right is null)
        {
            return;
        }

        if (left.IsDir && !right.IsDir)
        {
            left = null;
            return;
        }

        if (!left.IsDir && right.IsDir)
        {
            right = null;
        }
    }

    private static Built BothFiles(
        string rel,
        string leftRoot,
        string rightRoot,
        SyncEntry left,
        SyncEntry right,
        SyncOptions options)
    {
        var status = SyncRules.Classify(
            left.Side,
            right.Side,
            Path.Combine(leftRoot, rel),
            Path.Combine(rightRoot, rel),
            options);
        if (status.Error is not null)
        {
            return Built.Fail(status.Error);
        }

        return Built.Row(rel, false, left.Side, right.Side, status.Status, options.Asymmetric);
    }

    private static Built OneSide(string rel, SyncStatus status, SyncEntry entry, bool leftSide, bool asymmetric)
    {
        var left = leftSide ? entry.Side : null;
        var right = leftSide ? null : entry.Side;
        return Built.Row(rel, entry.IsDir, left, right, status, asymmetric);
    }

    private static FileResult<IReadOnlyList<SyncItem>> Fail(OperationError error) =>
        FileResult<IReadOnlyList<SyncItem>>.Fail(error);

    private readonly struct Built
    {
        private Built(SyncItem? item, OperationError? error)
        {
            Item = item;
            Error = error;
        }

        public SyncItem? Item { get; }

        public OperationError? Error { get; }

        public static Built Row(
            string rel,
            bool isDir,
            SyncSide? left,
            SyncSide? right,
            SyncStatus status,
            bool asymmetric)
        {
            var action = SyncRules.DefaultAction(status, asymmetric);
            return new Built(new SyncItem(rel, isDir, left, right, status, action), null);
        }

        public static Built Fail(OperationError error) => new(null, error);
    }
}
