namespace D3skCommander.Core.FileSystem;

/// <summary>Reads what <c>udisksctl</c> prints.</summary>
public static class Udisks
{
    /// <summary>The folder in <c>Mounted /dev/sdb1 at /media/user/USB.</c> (spaces allowed, trailing dot dropped), else null.</summary>
    public static string? ParseMounted(string stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        foreach (var line in stdout.Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith("Mounted ", StringComparison.Ordinal))
            {
                continue;
            }

            const string at = " at ";
            var cut = text.IndexOf(at, StringComparison.Ordinal);
            if (cut < 0)
            {
                continue;
            }

            var path = text[(cut + at.Length)..].Trim();
            if (path.EndsWith('.'))
            {
                path = path[..^1];
            }

            if (path.StartsWith('/'))
            {
                return path;
            }
        }

        return null;
    }
}
