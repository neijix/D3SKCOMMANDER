using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

/// <summary>
/// Windows connections go through WNet, in memory. <c>CONNECT_TEMPORARY</c> keeps the
/// password out of the user profile. There is no process and no command line.
/// </summary>
internal static class WindowsNet
{
    private const int ResourceConnected = 0x1;
    private const int ResourceTypeDisk = 0x1;
    private const int ConnectTemporary = 0x4;
    private const int ErrorMoreData = 234;
    private const int ErrorNoMoreItems = 259;
    private const int SessionCredentialConflict = 1219;

    [SupportedOSPlatform("windows")]
    public static FileResult<string> Connect(Share share, Credentials credentials, string? letter)
    {
        var user = UserName(credentials);
        var password = credentials.Password.Length == 0 ? null : credentials.Password;
        var remote = Wide(share.Unc());
        var local = Wide(letter);
        var userPtr = Wide(user);
        var passwordPtr = Wide(password);
        try
        {
            var resource = new NetResource
            {
                Type = ResourceTypeDisk,
                LocalName = local,
                RemoteName = remote,
            };
            var code = WNetAddConnection2(ref resource, passwordPtr, userPtr, ConnectTemporary);
            if (code != 0 && !(code == SessionCredentialConflict && letter is null))
            {
                return FileResult<string>.Fail(OperationError(code));
            }

            var root = letter is null ? share.Unc() + "\\" : letter + "\\";
            return FileResult<string>.Ok(root);
        }
        finally
        {
            Free(remote);
            Free(local);
            Free(userPtr);
            Free(passwordPtr);
        }
    }

    [SupportedOSPlatform("windows")]
    public static FileResult<string> Disconnect(string path)
    {
        var root = NetDrive.WindowsRoot(path);
        if (root is null)
        {
            return FileResult<string>.Fail(Message(path + " is not on a network drive"));
        }

        var code = WNetCancelConnection2(root, 0, 0);
        if (code != 0)
        {
            return FileResult<string>.Fail(OperationError(code));
        }

        return FileResult<string>.Ok(root);
    }

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<ConnectedShare> List()
    {
        var code = WNetOpenEnum(ResourceConnected, ResourceTypeDisk, 0, IntPtr.Zero, out var handle);
        if (code != 0)
        {
            return [];
        }

        try
        {
            return Read(handle);
        }
        finally
        {
            _ = WNetCloseEnum(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static List<ConnectedShare> Read(IntPtr handle)
    {
        var shares = new List<ConnectedShare>();
        var capacity = 16 * 1024;
        var buffer = Marshal.AllocHGlobal(capacity);
        try
        {
            while (true)
            {
                var count = -1;
                var bytes = capacity;
                var code = WNetEnumResource(handle, ref count, buffer, ref bytes);
                if (code == ErrorNoMoreItems || (code == 0 && count <= 0))
                {
                    return shares;
                }

                if (code == ErrorMoreData)
                {
                    buffer = Grow(buffer, ref capacity, bytes);
                    continue;
                }

                if (code != 0)
                {
                    return shares;
                }

                Append(shares, buffer, count);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr Grow(IntPtr buffer, ref int capacity, int required)
    {
        var next = required > capacity ? required : capacity * 2;
        Marshal.FreeHGlobal(buffer);
        capacity = next;
        return Marshal.AllocHGlobal(capacity);
    }

    private static void Append(List<ConnectedShare> shares, IntPtr buffer, int count)
    {
        var stride = Marshal.SizeOf<NetResource>();
        for (var i = 0; i < count; i++)
        {
            var item = Marshal.PtrToStructure<NetResource>(IntPtr.Add(buffer, i * stride));
            var remote = ReadString(item.RemoteName);
            if (remote is null)
            {
                continue;
            }

            var local = ReadString(item.LocalName);
            shares.Add(new ConnectedShare(remote, local ?? remote));
        }
    }

    private static string? UserName(Credentials credentials)
    {
        if (credentials.User.Length == 0)
        {
            return null;
        }

        if (credentials.Domain.Length == 0)
        {
            return credentials.User;
        }

        return credentials.Domain + "\\" + credentials.User;
    }

    private static IntPtr Wide(string? text) =>
        string.IsNullOrEmpty(text) ? IntPtr.Zero : Marshal.StringToHGlobalUni(text);

    private static void Free(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static string? ReadString(IntPtr pointer) =>
        pointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(pointer);

    private static D3skCommander.Core.Common.OperationError OperationError(int code) =>
        D3skCommander.Core.Common.OperationError.FromMessage(new Win32Exception(code).Message);

    private static D3skCommander.Core.Common.OperationError Message(string message) =>
        D3skCommander.Core.Common.OperationError.FromMessage(message);

    [StructLayout(LayoutKind.Sequential)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public IntPtr LocalName;
        public IntPtr RemoteName;
        public IntPtr Comment;
        public IntPtr Provider;
    }

    // DllImport: LibraryImport needs AllowUnsafeBlocks, which this library does not set.
    [SupportedOSPlatform("windows")]
    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2W")]
    private static extern int WNetAddConnection2(ref NetResource resource, IntPtr password, IntPtr user, int flags);

    [SupportedOSPlatform("windows")]
    [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetCancelConnection2W")]
    private static extern int WNetCancelConnection2(string name, int flags, int force);

    [SupportedOSPlatform("windows")]
    [DllImport("mpr.dll", EntryPoint = "WNetOpenEnumW")]
    private static extern int WNetOpenEnum(int scope, int type, int usage, IntPtr resource, out IntPtr handle);

    [SupportedOSPlatform("windows")]
    [DllImport("mpr.dll", EntryPoint = "WNetEnumResourceW")]
    private static extern int WNetEnumResource(IntPtr handle, ref int count, IntPtr buffer, ref int size);

    [SupportedOSPlatform("windows")]
    [DllImport("mpr.dll", EntryPoint = "WNetCloseEnum")]
    private static extern int WNetCloseEnum(IntPtr handle);
}
