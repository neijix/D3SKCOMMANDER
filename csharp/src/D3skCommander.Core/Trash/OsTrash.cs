using D3skCommander.Core.Common;

namespace D3skCommander.Core.Trash;

/// <summary>
/// Moves a file or directory into the OS trash. A directory goes as one item.
/// Windows uses the shell recycle bin, macOS <c>NSFileManager</c>, Linux <c>g_file_trash</c>.
/// </summary>
public static class OsTrash
{
    /// <summary>Reported when this OS cannot trash (no libgio, or the volume refuses trash).</summary>
    public const string UnavailableMessage = "trash unavailable";

    public static TrashStatus MoveToTrash(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!Exists(path))
        {
            return TrashStatus.Fail(OperationError.NotFound(path));
        }

        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return WindowsTrash.Move(full);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacTrash.Move(full);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxTrash.Move(full);
        }

        return TrashStatus.Unavailable(full);
    }

    private static bool Exists(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            return true;
        }

        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
