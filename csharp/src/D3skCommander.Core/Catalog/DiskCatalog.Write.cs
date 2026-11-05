using System.Text;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Catalog;

public static partial class DiskCatalog
{
    /// <summary>
    /// Walk <paramref name="root"/> and write its catalog. The output file is skipped when it sits inside the root.
    /// Cancel, or any other failure, deletes the output.
    /// </summary>
    public static CatalogWriteResult Write(
        string root,
        string output,
        CancellationToken cancellationToken,
        IProgress<JobProgress>? progress = null,
        DateTimeOffset? created = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(output);
        var stamp = created ?? DateTimeOffset.UtcNow;
        StreamWriter? writer = null;
        try
        {
            var problem = CheckRoot(root, output);
            if (problem is not null)
            {
                return CatalogWriteResult.Fail(problem);
            }

            var storedRoot = NormalizeRoot(root);
            writer = Open(output);
            CatalogJson.WriteHeader(writer, storedRoot, stamp);
            cancellationToken.ThrowIfCancellationRequested();
            var count = WriteEntries(writer, root, Path.GetFullPath(output), cancellationToken, progress);
            writer.Flush();
            return CatalogWriteResult.Ok(count);
        }
        catch (Exception ex)
        {
            Abandon(ref writer, output);
            if (ex is OperationCanceledException)
            {
                return CatalogWriteResult.Fail(OperationError.Cancelled(output));
            }

            return CatalogWriteResult.Fail(OperationError.FromException(ex, output));
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static int WriteEntries(
        StreamWriter writer,
        string root,
        string outputFull,
        CancellationToken cancellationToken,
        IProgress<JobProgress>? progress)
    {
        var count = 0;
        long bytes = 0;
        var stack = new Stack<WalkFrame>();
        stack.Push(new WalkFrame(Children(root)));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = stack.Peek();
            if (frame.Index >= frame.Children.Length)
            {
                stack.Pop();
                continue;
            }

            var child = frame.Children[frame.Index];
            frame.Index++;
            if (SameFile(child, outputFull) || !TryDescribe(root, child, out var entry))
            {
                continue;
            }

            CatalogJson.WriteEntry(writer, entry);
            count++;
            bytes += entry.Size;
            Report(progress, count, bytes, entry.Path);
            if (entry.IsDir)
            {
                stack.Push(new WalkFrame(Children(child)));
            }
        }

        return count;
    }

    private static OperationError? CheckRoot(string root, string output)
    {
        if (!Directory.Exists(root))
        {
            if (File.Exists(root))
            {
                return OperationError.InvalidData("not a directory", root);
            }

            return OperationError.NotFound(root);
        }

        var fullOutput = Path.GetFullPath(output);
        if (Directory.Exists(fullOutput))
        {
            return OperationError.InvalidData("not a file", output);
        }

        var parent = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            return OperationError.NotFound(parent);
        }

        return null;
    }

    private static string NormalizeRoot(string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    private static StreamWriter Open(string output)
    {
        var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void Abandon(ref StreamWriter? writer, string output)
    {
        var open = writer;
        writer = null;
        if (open is null)
        {
            return;
        }

        try
        {
            open.Dispose();
        }
        catch (IOException)
        {
            // The partial file is removed next.
        }

        try
        {
            File.Delete(output);
        }
        catch (IOException)
        {
            // A failed remove still reports the original error.
        }
    }

    private static string[] Children(string directory)
    {
        try
        {
            var children = Directory.GetFileSystemEntries(directory, "*", Listing());
            Array.Sort(children, CompareNames);
            return children;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return [];
        }
    }

    private static EnumerationOptions Listing()
    {
        var options = new EnumerationOptions();
        options.AttributesToSkip = 0;
        options.IgnoreInaccessible = true;
        options.RecurseSubdirectories = false;
        options.ReturnSpecialDirectories = false;
        return options;
    }

    private static int CompareNames(string left, string right) =>
        string.Compare(Path.GetFileName(left), Path.GetFileName(right), StringComparison.Ordinal);

    private static bool TryDescribe(string root, string child, out CatalogEntry entry)
    {
        entry = null!;
        try
        {
            var relative = RelativePath(root, child);
            if (relative is null)
            {
                return false;
            }

            var link = IsReparsePoint(child);
            var directory = !link && Directory.Exists(child);
            var size = directory ? 0 : new FileInfo(child).Length;
            entry = new CatalogEntry(relative, size, LastWrite(child), directory);
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    private static string? RelativePath(string root, string child)
    {
        var relative = Path.GetRelativePath(root, child);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        return relative.Replace('\\', '/');
    }

    private static DateTimeOffset LastWrite(string path)
    {
        var utc = File.GetLastWriteTimeUtc(path);
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    private static bool SameFile(string child, string outputFull)
    {
        try
        {
            return string.Equals(Path.GetFullPath(child), outputFull, PathComparison());
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    private static void Report(IProgress<JobProgress>? progress, int count, long bytes, string name) =>
        progress?.Report(new JobProgress(count, count, bytes, bytes, name));

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsFileError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private sealed class WalkFrame
    {
        public WalkFrame(string[] children) => Children = children;

        public string[] Children { get; }

        public int Index { get; set; }
    }
}
