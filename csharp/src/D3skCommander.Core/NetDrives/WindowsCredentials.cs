using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

/// <summary>
/// Hands a server login to Windows Credential Manager (<c>CredWrite</c>, a domain password for the server name,
/// kept on this machine), as <c>cmdkey /add</c> does: Windows then logs in to that server by itself, for this app
/// and every other. The app keeps no copy; the entry is removed in Credential Manager.
/// </summary>
internal static class WindowsCredentials
{
    private const int TypeDomainPassword = 2;
    private const int PersistLocalMachine = 2;

    [SupportedOSPlatform("windows")]
    public static FileResult<string> Save(string server, string user, string password)
    {
        var target = Marshal.StringToHGlobalUni(server);
        var userPtr = Marshal.StringToHGlobalUni(user);
        var blob = Marshal.StringToHGlobalUni(password);
        try
        {
            var credential = new Credential
            {
                Type = TypeDomainPassword,
                TargetName = target,
                CredentialBlobSize = password.Length * sizeof(char),
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = userPtr,
            };
            if (!CredWrite(ref credential, 0))
            {
                var message = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return FileResult<string>.Fail(OperationError.FromMessage(message, server));
            }

            return FileResult<string>.Ok(server);
        }
        finally
        {
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(userPtr);
            Marshal.ZeroFreeGlobalAllocUnicode(blob);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public int LastWrittenLow;
        public int LastWrittenHigh;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    // DllImport: LibraryImport needs AllowUnsafeBlocks, which this library does not set.
    [SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);
}
