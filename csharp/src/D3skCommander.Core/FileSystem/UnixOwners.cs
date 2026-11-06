using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace D3skCommander.Core.FileSystem;

/// <summary>
/// Owner and group names of a file on Linux and macOS: one <c>statx</c> (Linux) or <c>lstat</c> (macOS) for the ids,
/// <c>getpwuid</c>/<c>getgrgid</c> for the names, cached. A failure (no such file, an odd libc) gives null; on Windows always null.
/// </summary>
public static class UnixOwners
{
    private const int AtFdcwd = -100;
    private const int AtSymlinkNofollow = 0x100;
    private const uint StatxUidGid = 0x8 | 0x10;
    private static readonly ConcurrentDictionary<uint, string> Users = new();
    private static readonly ConcurrentDictionary<uint, string> Groups = new();
    private static readonly object Lookup = new();

    public static string? OwnerOf(string path) => Names(path, group: false);

    public static string? GroupOf(string path) => Names(path, group: true);

    private static string? Names(string path, bool group)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            if (!TryIds(path, out var uid, out var gid))
            {
                return null;
            }

            var id = group ? gid : uid;
            var cache = group ? Groups : Users;
            if (cache.TryGetValue(id, out var known))
            {
                return known;
            }

            var name = NameOf(id, group) ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            cache[id] = name;
            return name;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return null;
        }
    }

    private static bool TryIds(string path, out uint uid, out uint gid)
    {
        var buffer = new byte[256];
        uid = 0;
        gid = 0;
        if (OperatingSystem.IsLinux())
        {
            if (Linux.Statx(AtFdcwd, path, AtSymlinkNofollow, StatxUidGid, buffer) != 0)
            {
                return false;
            }

            // struct statx: stx_nlink at 16, stx_uid at 20, stx_gid at 24 on every architecture.
            uid = BitConverter.ToUInt32(buffer, 20);
            gid = BitConverter.ToUInt32(buffer, 24);
            return true;
        }

        var ok = RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? Mac.LstatIno64(path, buffer) == 0
            : Mac.Lstat(path, buffer) == 0;
        if (!ok)
        {
            return false;
        }

        // struct stat (64-bit inode): st_uid at 16, st_gid at 20.
        uid = BitConverter.ToUInt32(buffer, 16);
        gid = BitConverter.ToUInt32(buffer, 20);
        return true;
    }

    // getpwuid and getgrgid return a static buffer: one caller at a time. pw_name and gr_name are the first field.
    private static string? NameOf(uint id, bool group)
    {
        lock (Lookup)
        {
            IntPtr record;
            if (OperatingSystem.IsLinux())
            {
                record = group ? Linux.Getgrgid(id) : Linux.Getpwuid(id);
            }
            else
            {
                record = group ? Mac.Getgrgid(id) : Mac.Getpwuid(id);
            }

            if (record == IntPtr.Zero)
            {
                return null;
            }

            var name = Marshal.ReadIntPtr(record);
            return name == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(name);
        }
    }

    // DllImport: LibraryImport needs AllowUnsafeBlocks, which this library does not set.
    private static class Linux
    {
        [DllImport("libc.so.6", EntryPoint = "statx")]
        public static extern int Statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, byte[] buffer);

        [DllImport("libc.so.6", EntryPoint = "getpwuid")]
        public static extern IntPtr Getpwuid(uint uid);

        [DllImport("libc.so.6", EntryPoint = "getgrgid")]
        public static extern IntPtr Getgrgid(uint gid);
    }

    private static class Mac
    {
        [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "lstat")]
        public static extern int Lstat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buffer);

        [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "lstat$INODE64")]
        public static extern int LstatIno64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buffer);

        [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "getpwuid")]
        public static extern IntPtr Getpwuid(uint uid);

        [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "getgrgid")]
        public static extern IntPtr Getgrgid(uint gid);
    }
}
