namespace D3skCommander.Core.Update;

/// <summary>
/// Replaces a program file by renaming a copy that sits beside it.
/// On Windows the running file moves to <c>&lt;name&gt;.old</c> first.
/// </summary>
public static class FileSwap
{
    public static void Replace(string source, string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(target);
        var staged = Sibling(target, ".update-new");
        File.Copy(source, staged, overwrite: true);
        try
        {
            MakeExecutable(staged);
            MoveAsideOnWindows(target);
            MoveIntoPlace(staged, target);
        }
        catch (Exception)
        {
            DeleteFile(staged);
            throw;
        }
    }

    /// <summary>Removes the Windows <c>.old</c> program left beside <paramref name="exe"/>.</summary>
    public static void RemoveLeftovers(string exe)
    {
        ArgumentException.ThrowIfNullOrEmpty(exe);
        DeleteFile(Sibling(exe, ".old"));
    }

    public static string Sibling(string path, string suffix)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(suffix);
        var name = Path.GetFileName(path) + suffix;
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return name;
        }

        return Path.Combine(directory, name);
    }

    /// <summary>The scratch folder beside <paramref name="path"/>, on the same file system as the final rename.</summary>
    public static string ScratchDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            parent = ".";
        }

        return Path.Combine(parent, ".d3sk-update");
    }

    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // v0.1 ignores a failure to remove the scratch folder.
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static void MoveAsideOnWindows(string target)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(target))
        {
            return;
        }

        var old = Sibling(target, ".old");
        DeleteFile(old);
        File.Move(target, old);
    }

    private static void MoveIntoPlace(string staged, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            File.Move(staged, target);
            return;
        }

        File.Move(staged, target, overwrite: true);
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next start tries again.
        }
    }
}
