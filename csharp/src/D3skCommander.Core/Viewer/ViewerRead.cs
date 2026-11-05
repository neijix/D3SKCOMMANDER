using D3skCommander.Core.Common;

namespace D3skCommander.Core.Viewer;

/// <summary>The first <see cref="ViewerLimits.MaxRead"/> bytes of a file, or the error that stopped the read.</summary>
public readonly record struct ViewerRead(byte[] Bytes, bool Truncated, OperationError? Error)
{
    public static ViewerRead Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            using var stream = File.OpenRead(path);
            return FromStream(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ViewerRead([], false, OperationError.FromException(ex, path));
        }
    }

    /// <summary>Opens <paramref name="path"/> read-only and shared, so a file another program writes can still be viewed.</summary>
    internal static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);

    /// <summary>Reads up to <paramref name="count"/> bytes at <paramref name="position"/>; fewer only at the end of the file.</summary>
    internal static int ReadAt(Stream stream, long position, byte[] buffer, int count)
    {
        stream.Seek(position, SeekOrigin.Begin);
        var filled = 0;
        while (filled < count)
        {
            var read = stream.Read(buffer, filled, count - filled);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static ViewerRead FromStream(Stream stream)
    {
        var limit = ViewerLimits.MaxRead + 1;
        var buffer = new byte[limit];
        var filled = 0;
        while (filled < limit)
        {
            var read = stream.Read(buffer, filled, limit - filled);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        var truncated = filled > ViewerLimits.MaxRead;
        var length = truncated ? ViewerLimits.MaxRead : filled;
        if (length != buffer.Length)
        {
            Array.Resize(ref buffer, length);
        }

        return new ViewerRead(buffer, truncated, null);
    }
}
