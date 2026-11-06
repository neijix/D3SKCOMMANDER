using System.Runtime.Versioning;

namespace D3skCommander.Core.FileSystem;

/// <summary>Mode bits from the followed file. Callers skip this on Windows.</summary>
[UnsupportedOSPlatform("windows")]
internal static class UnixFiles
{
    public static bool IsReadOnly(string path)
    {
        var mode = File.GetUnixFileMode(path);
        return (mode & UnixFileMode.UserWrite) == 0;
    }

    public static string Attributes(string path) => Format(File.GetUnixFileMode(path));

    /// <summary>The mode an enumeration already read (no new system call).</summary>
    public static UnixFileMode ModeOf(FileSystemInfo info) => info.UnixFileMode;

    public static string Format(UnixFileMode mode)
    {
        var bits = (int)mode;
        var chars = new char[9];
        var index = 0;
        var shift = 6;
        while (shift >= 0)
        {
            var part = (bits >> shift) & 7;
            chars[index] = (part & 4) != 0 ? 'r' : '-';
            chars[index + 1] = (part & 2) != 0 ? 'w' : '-';
            chars[index + 2] = (part & 1) != 0 ? 'x' : '-';
            index += 3;
            shift -= 3;
        }

        return new string(chars);
    }
}
