using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

/// <summary>The shares a server offers, as the panel lists them at <c>\\server\</c>.</summary>
public interface IServerShares
{
    /// <summary>The disk shares of <paramref name="server"/>, hidden (<c>$</c>) ones left out. Blocks until the server answers.</summary>
    FileResult<IReadOnlyList<string>> Shares(string server);
}

/// <summary>Windows: <c>NetShareEnum</c> over the current session (a connection made with a user and password counts).</summary>
public sealed class OsServerShares : IServerShares
{
    public FileResult<IReadOnlyList<string>> Shares(string server)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        if (!OperatingSystem.IsWindows())
        {
            return FileResult<IReadOnlyList<string>>.Fail(OperationError.Unsupported("listing the shares of a server needs Windows", UncPath.ServerRoot(server)));
        }

        return WindowsShares.List(server);
    }
}

internal static class WindowsShares
{
    private const int MaxPreferredLength = -1;
    private const int ErrorMoreData = 234;
    private const int ErrorAccessDenied = 5;
    private const int ErrorLogonFailure = 1326;

    [SupportedOSPlatform("windows")]
    public static FileResult<IReadOnlyList<string>> List(string server)
    {
        var root = UncPath.ServerRoot(server);
        var resume = 0;
        var code = NetShareEnum(server, 1, out var buffer, MaxPreferredLength, out var read, out _, ref resume);
        try
        {
            if (code != 0 && code != ErrorMoreData)
            {
                return FileResult<IReadOnlyList<string>>.Fail(Error(code, root));
            }

            return FileResult<IReadOnlyList<string>>.Ok(Read(buffer, read));
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                _ = NetApiBufferFree(buffer);
            }
        }
    }

    private static List<string> Read(IntPtr buffer, int count)
    {
        var names = new List<string>(count);
        var stride = Marshal.SizeOf<ShareInfo1>();
        for (var i = 0; i < count; i++)
        {
            var info = Marshal.PtrToStructure<ShareInfo1>(IntPtr.Add(buffer, i * stride));
            var name = info.NetName == IntPtr.Zero ? null : Marshal.PtrToStringUni(info.NetName);
            if (!string.IsNullOrEmpty(name) && NetDrive.IsBrowsableShare(info.Type, name))
            {
                names.Add(name);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static OperationError Error(int code, string root)
    {
        var message = new Win32Exception(code).Message;
        return code is ErrorAccessDenied or ErrorLogonFailure
            ? OperationError.AccessDenied(message, root)
            : OperationError.Network(message, root);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1
    {
        public IntPtr NetName;
        public uint Type;
        public IntPtr Remark;
    }

    // DllImport: LibraryImport needs AllowUnsafeBlocks, which this library does not set.
    [SupportedOSPlatform("windows")]
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string serverName, int level, out IntPtr buffer, int preferredMaxLength, out int entriesRead, out int totalEntries, ref int resumeHandle);

    [SupportedOSPlatform("windows")]
    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
