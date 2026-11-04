namespace D3skCommander.Core.FileSystem;

/// <summary>Totals of a folder tree, not counting the folder itself.</summary>
public readonly record struct FolderSize(ulong Bytes, ulong Files, ulong Directories);

public static partial class LocalFileSystem
{
    /// <summary>Bytes, files and folders below <paramref name="directory"/>. Stops early when cancelled.</summary>
    public static FolderSize DirectorySize(string directory, CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ulong bytes = 0;
        ulong files = 0;
        ulong directories = 0;
        Measure(directory, cancel, ref bytes, ref files, ref directories);
        return new FolderSize(bytes, files, directories);
    }

    private static bool Measure(
        string directory,
        CancellationToken cancel,
        ref ulong bytes,
        ref ulong files,
        ref ulong directories)
    {
        var read = ReadChildren(directory);
        if (!read.IsSuccess)
        {
            return true;
        }

        foreach (var child in read.Value)
        {
            if (cancel.IsCancellationRequested)
            {
                return false;
            }

            if (IsRealDirectory(child))
            {
                directories++;
                if (!Measure(child, cancel, ref bytes, ref files, ref directories))
                {
                    return false;
                }

                continue;
            }

            files++;
            bytes += LengthOf(child);
        }

        return true;
    }
}
