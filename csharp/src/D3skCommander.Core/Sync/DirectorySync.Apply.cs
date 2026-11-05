using D3skCommander.Core.Common;

namespace D3skCommander.Core.Sync;

public static partial class DirectorySync
{
    /// <summary>
    /// Apply <paramref name="items"/> in order. <paramref name="progress"/> is told each relative path
    /// before it runs; returning false stops, and so does cancelling. Copies keep the modification time.
    /// Deleting a directory removes it only when it is empty.
    /// </summary>
    public static SyncReport Apply(
        string left,
        string right,
        IReadOnlyList<SyncItem> items,
        CancellationToken cancellationToken,
        Func<string, bool> progress)
    {
        ArgumentException.ThrowIfNullOrEmpty(left);
        ArgumentException.ThrowIfNullOrEmpty(right);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(progress);

        var report = new SyncReport();
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("An item is missing.", nameof(items));
            }

            if (item.Action == SyncAction.None)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested || !progress(item.Rel))
            {
                break;
            }

            ApplyOne(left, right, item, report);
        }

        return report;
    }

    private static void ApplyOne(string left, string right, SyncItem item, SyncReport report)
    {
        if (!IsSafeRel(item.Rel))
        {
            report.Add(OperationError.InvalidData("unsafe path: " + item.Rel, item.Rel));
            return;
        }

        var leftPath = Path.Combine(left, item.Rel);
        var rightPath = Path.Combine(right, item.Rel);
        var error = RunAction(item, leftPath, rightPath);
        if (error is not null)
        {
            report.Add(error);
            return;
        }

        if (Deletes(item.Action))
        {
            report.Deleted++;
            return;
        }

        report.Copied++;
    }

    private static bool Deletes(SyncAction action) =>
        action == SyncAction.DeleteLeft || action == SyncAction.DeleteRight;

    private static OperationError? RunAction(SyncItem item, string leftPath, string rightPath)
    {
        if (item.Action == SyncAction.CopyToRight)
        {
            return CopyItem(leftPath, rightPath, item.IsDir);
        }

        if (item.Action == SyncAction.CopyToLeft)
        {
            return CopyItem(rightPath, leftPath, item.IsDir);
        }

        if (item.Action == SyncAction.DeleteRight)
        {
            return DeleteItem(rightPath, item.IsDir);
        }

        if (item.Action == SyncAction.DeleteLeft)
        {
            return DeleteItem(leftPath, item.IsDir);
        }

        return null;
    }

    private static OperationError? CopyItem(string source, string destination, bool isDir)
    {
        try
        {
            if (isDir)
            {
                Directory.CreateDirectory(destination);
                return null;
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            return null;
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return SyncIo.At(destination, ex);
        }
    }

    private static OperationError? DeleteItem(string path, bool isDir)
    {
        try
        {
            if (isDir)
            {
                Directory.Delete(path, recursive: false);
            }
            else
            {
                File.Delete(path);
            }

            return null;
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return SyncIo.At(path, ex);
        }
    }

    // A relative path of plain names: never absolute, never "." or "..".
    private static bool IsSafeRel(string rel)
    {
        if (rel.Length == 0 || Path.IsPathRooted(rel))
        {
            return false;
        }

        var named = false;
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }

            if (part == "." || part == "..")
            {
                return false;
            }

            named = true;
        }

        return named;
    }
}
