using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.TextDocument;

/// <summary>
/// Writes to <c>.{name}.d3sk-tmp</c> beside the file, then renames it over the target.
/// A failure deletes the temp and leaves the old file.
/// </summary>
public static class TextAtomicWrite
{
    public static FileStatus Write(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? temp = null;
        try
        {
            var name = Path.GetFileName(path);
            if (name.Length == 0)
            {
                return FileStatus.Fail(OperationError.InvalidData("no file name", path));
            }

            var directory = Path.GetDirectoryName(path);
            var folder = string.IsNullOrEmpty(directory) ? "." : directory;
            temp = Path.Combine(folder, "." + name + ".d3sk-tmp");
            WriteTemp(temp, bytes);
            CopyMode(path, temp);
            File.Move(temp, path, overwrite: true);
            temp = null;
            return FileStatus.Success;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            DeleteTemp(temp);
            return FileStatus.Fail(OperationError.FromException(ex, path));
        }
    }

    private static void WriteTemp(string temp, ReadOnlySpan<byte> bytes)
    {
        ClearReadOnly(temp);
        using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void CopyMode(string path, string temp)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                CopyReadOnly(path, temp);
                return;
            }

            File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            // v0.1 ignores a permissions copy that fails.
        }
    }

    private static void CopyReadOnly(string path, string temp)
    {
        var source = File.GetAttributes(path);
        if ((source & FileAttributes.ReadOnly) == 0)
        {
            return;
        }

        File.SetAttributes(temp, File.GetAttributes(temp) | FileAttributes.ReadOnly);
    }

    private static void DeleteTemp(string? temp)
    {
        if (string.IsNullOrEmpty(temp))
        {
            return;
        }

        try
        {
            ClearReadOnly(temp);
            File.Delete(temp);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            // The write error is the one the caller sees.
        }
    }

    /// <summary>A temp that carries the target's ReadOnly flag can neither be rewritten nor deleted until cleared.</summary>
    private static void ClearReadOnly(string temp)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(temp))
        {
            return;
        }

        var attributes = File.GetAttributes(temp);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(temp, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
