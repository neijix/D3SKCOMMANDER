using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>Copies mtime, then permissions. A read-only file could not be re-dated the other way around.</summary>
internal static class LocalMetadata
{
    public static void Apply(
        IFileSystemProvider sourceProvider,
        string source,
        IFileSystemProvider destinationProvider,
        string destination)
    {
        if (destinationProvider is not LocalFileSystemProvider)
        {
            return;
        }

        try
        {
            if (sourceProvider is LocalFileSystemProvider)
            {
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            }
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            // Some file systems keep neither time nor mode.
        }

        ApplyMode(sourceProvider, source, destination);
    }

    public static bool BothLocal(IFileSystemProvider source, IFileSystemProvider destination) =>
        source is LocalFileSystemProvider && destination is LocalFileSystemProvider;

    public static bool IsLink(string path)
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

    private static void ApplyMode(IFileSystemProvider sourceProvider, string source, string destination)
    {
        if (OperatingSystem.IsWindows() || sourceProvider is not LocalFileSystemProvider)
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            // Best effort, same as v0.1 apply_metadata.
        }
    }

    internal static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
