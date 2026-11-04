using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

public sealed partial class LocalFileSystemProvider
{
    protected override Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken)
    {
        if (Cancelled(cancellationToken, path, out var error))
        {
            return Status(error!);
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        var invalid = LocalFileSystem.ValidateName(name);
        if (invalid is not null)
        {
            return Status(invalid with { Path = path });
        }

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return Status(OperationError.NotFound(path));
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            return Status(OperationError.AlreadyExists(path));
        }

        try
        {
            Directory.CreateDirectory(path);
            return Done();
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return Status(OperationError.FromException(ex, path));
        }
    }

    protected override Task<FileResult<string>> RenameCore(
        string path,
        string newName,
        CancellationToken cancellationToken)
    {
        if (Cancelled(cancellationToken, path, out var error))
        {
            return Task.FromResult(FileResult<string>.Fail(error!));
        }

        return Task.FromResult(LocalFileSystem.Rename(path, newName));
    }

    protected override Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken)
    {
        if (Cancelled(cancellationToken, path, out var error))
        {
            return Status(error!);
        }

        try
        {
            var failure = DeletePath(path);
            return failure is null ? Done() : Status(failure);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return Status(OperationError.FromException(ex, path));
        }
    }

    // A link is removed itself. Directory.Delete would follow a directory link into the target.
    // On Windows, File.Delete calls DeleteFile, which refuses a directory reparse point.
    private static OperationError? DeletePath(string path)
    {
        if (OperatingSystem.IsWindows() && IsDirectoryReparsePoint(path))
        {
            RemoveDirectoryLink(path);
            return null;
        }

        if (IsLink(path))
        {
            File.Delete(path);
            return null;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return null;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
            return null;
        }

        return OperationError.NotFound(path);
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool IsDirectoryReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0
                && (attributes & FileAttributes.Directory) != 0;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    // RemoveDirectory drops the reparse point. DeleteFile (File.Delete) returns access denied.
    [SupportedOSPlatform("windows")]
    private static void RemoveDirectoryLink(string path)
    {
        if (RemoveDirectoryW(path))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        throw new IOException(new Win32Exception(error).Message);
    }

    // DllImport: LibraryImport needs AllowUnsafeBlocks, which this library does not set.
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "RemoveDirectoryW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDirectoryW(string lpPathName);
}
