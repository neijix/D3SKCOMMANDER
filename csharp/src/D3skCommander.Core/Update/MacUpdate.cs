using D3skCommander.Core.Archives;

namespace D3skCommander.Core.Update;

/// <summary>Swaps a <c>.app</c> bundle for the one inside the archive, keeping the old one until the new one is in place.</summary>
internal static class MacUpdate
{
    public static void ReplaceBundle(string archive, string bundle)
    {
        var dir = FileSwap.ScratchDirectory(bundle);
        FileSwap.DeleteDirectory(dir);
        var extracted = ArchiveIo.Extract(archive, null, dir);
        if (extracted.Error is not null)
        {
            throw new IOException(extracted.Error.Message);
        }

        var found = FindApp(dir);
        if (found is null)
        {
            throw new IOException("no .app in the update");
        }

        var old = FileSwap.Sibling(bundle, ".old");
        FileSwap.DeleteDirectory(old);
        Directory.Move(bundle, old);
        try
        {
            Directory.Move(found, bundle);
        }
        catch (Exception)
        {
            Restore(old, bundle);
            throw;
        }

        FileSwap.DeleteDirectory(old);
        FileSwap.DeleteDirectory(dir);
    }

    private static string? FindApp(string root)
    {
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(Path.GetExtension(path), ".app", StringComparison.Ordinal))
            {
                return path;
            }
        }

        return null;
    }

    private static void Restore(string old, string bundle)
    {
        try
        {
            if (Directory.Exists(old) && !Directory.Exists(bundle))
            {
                Directory.Move(old, bundle);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The previous bundle stays beside the target.
        }
    }
}
