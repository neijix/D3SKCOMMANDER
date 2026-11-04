using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileSystem;

public static partial class LocalFileSystem
{
    /// <summary>Creates an empty file. An existing path is <see cref="ErrorKind.AlreadyExists"/>.</summary>
    public static FileStatus CreateFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return FileStatus.Success;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return FileStatus.Fail(CreateError(path, ex));
        }
    }

    /// <summary>Renames inside the same folder.</summary>
    public static FileResult<string> Rename(string path, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(newName);
        var invalid = ValidateName(newName);
        if (invalid is not null)
        {
            return FileResult<string>.Fail(invalid with { Path = path });
        }

        var target = WithFileName(path, newName);
        if (Exists(target) && !SameIgnoringCase(path, target))
        {
            return FileResult<string>.Fail(
                OperationError.AlreadyExists(target, "a file with that name already exists"));
        }

        try
        {
            Move(path, target);
            return FileResult<string>.Ok(target);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return FileResult<string>.Fail(OperationError.FromException(ex, path));
        }
    }

    private static OperationError CreateError(string path, Exception exception)
    {
        var error = OperationError.FromException(exception, path);
        if (error.Kind == ErrorKind.AlreadyExists || Exists(path))
        {
            return OperationError.AlreadyExists(path);
        }

        return error;
    }

    private static void Move(string path, string target)
    {
        if (IsRealDirectory(path))
        {
            Directory.Move(path, target);
            return;
        }

        File.Move(path, target);
    }

    private static string WithFileName(string path, string newName)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return newName;
        }

        return Path.Combine(directory, newName);
    }

    private static bool SameIgnoringCase(string left, string right) =>
        string.Equals(left.ToLowerInvariant(), right.ToLowerInvariant(), StringComparison.Ordinal);
}
