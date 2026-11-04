using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileSystem;

/// <summary>
/// Local listing, names, order and formatting from v0.1.
/// Drive buttons and free-space queries are a later row.
/// </summary>
public static partial class LocalFileSystem
{
    /// <summary>The user profile folder, or <c>/</c> when the OS has none.</summary>
    public static string HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length == 0 ? "/" : home;
    }

    private static EnumerationOptions AllEntries() => new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static FileResult<List<string>> ReadChildren(string directory)
    {
        try
        {
            var children = new List<string>();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", AllEntries()))
            {
                children.Add(path);
            }

            return FileResult<List<string>>.Ok(children);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return FileResult<List<string>>.Fail(OperationError.FromException(ex, directory));
        }
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

    /// <summary>A real directory. A symlink is not one, so a walk does not follow it.</summary>
    private static bool IsRealDirectory(string path)
    {
        if (IsLink(path))
        {
            return false;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) != 0;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
