namespace D3skCommander.Core.Compare;

/// <summary>Writes a temp file beside <c>path</c> and renames it over the target. A failure leaves the target untouched.</summary>
internal static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);
        var name = Path.GetFileName(path);
        if (name.Length == 0)
        {
            throw new IOException("no file name");
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        var temp = Path.Combine(directory, "." + name + ".d3sk-tmp");
        try
        {
            WriteTemp(temp, bytes);
            CopyPermissions(path, temp);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            DeleteTemp(temp);
            throw;
        }
    }

    private static void WriteTemp(string temp, byte[] bytes)
    {
        using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void CopyPermissions(string source, string temp)
    {
        if (!File.Exists(source))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(temp, File.GetAttributes(source));
            return;
        }

        File.SetUnixFileMode(temp, File.GetUnixFileMode(source));
    }

    private static void DeleteTemp(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The failed save already reports the original error.
        }
    }
}
