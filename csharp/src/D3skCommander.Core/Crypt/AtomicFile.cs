using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Crypt;

/// <summary>
/// Writes a destination via a sibling temporary file and renames it into place.
/// A failure deletes the temporary file and leaves the destination untouched.
/// </summary>
internal static class AtomicFile
{
    public static FileStatus Write(string destination, bool ownerOnly, Func<Stream, FileStatus> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(write);
        var temp = TemporaryPath(destination);
        var committed = false;
        try
        {
            var status = WriteTemp(temp, ownerOnly, write);
            if (!status.IsSuccess)
            {
                return status;
            }

            File.Move(temp, destination, overwrite: true);
            if (ownerOnly)
            {
                OwnerMode.Apply(destination);
            }

            committed = true;
            return FileStatus.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FileStatus.Fail(OperationError.FromException(ex, destination));
        }
        finally
        {
            if (!committed)
            {
                TryDelete(temp);
            }
        }
    }

    public static string TemporaryPath(string destination)
    {
        var directory = Path.GetDirectoryName(destination);
        var name = "." + Path.GetFileName(destination) + ".tmp";
        if (string.IsNullOrEmpty(directory))
        {
            return name;
        }

        return Path.Combine(directory, name);
    }

    private static FileStatus WriteTemp(string temp, bool ownerOnly, Func<Stream, FileStatus> write)
    {
        using var stream = Open(temp, ownerOnly);
        var status = write(stream);
        if (!status.IsSuccess)
        {
            return status;
        }

        stream.Flush(flushToDisk: true);
        return FileStatus.Success;
    }

    private static FileStream Open(string path, bool ownerOnly)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (ownerOnly && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var stream = new FileStream(path, options);
        if (ownerOnly)
        {
            OwnerMode.Apply(path);
        }

        return stream;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
