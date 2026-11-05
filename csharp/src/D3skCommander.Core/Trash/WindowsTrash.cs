using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Trash;

/// <summary>
/// Recycle Bin through <c>SHFileOperationW</c> (<c>FO_DELETE</c> + <c>FOF_ALLOWUNDO</c>).
/// That is the call <c>Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile</c> wraps; the
/// VisualBasic framework reference is not added from this folder.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsTrash
{
    private const uint Delete = 3;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmation = 0x0010;
    private const ushort Silent = 0x0004;
    private const ushort NoErrorUi = 0x0400;

    public static TrashStatus Move(string path)
    {
        var from = MarshalPath(path);
        try
        {
            var op = new ShFileOp
            {
                Function = Delete,
                From = from,
                Flags = (ushort)(AllowUndo | NoConfirmation | Silent | NoErrorUi),
            };
            var code = SHFileOperation(ref op);
            if (code == 0 && op.Aborted == 0)
            {
                return TrashStatus.Success;
            }

            if (op.Aborted != 0)
            {
                return TrashStatus.Fail(OperationError.Cancelled(path));
            }

            return TrashStatus.Fail(OperationError.Other("trash failed (" + code + ")", path, code));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return TrashStatus.Unavailable(path);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    // Double-null terminated UTF-16, which SHFileOperation requires.
    private static IntPtr MarshalPath(string path)
    {
        var bytes = Encoding.Unicode.GetBytes(path + "\0\0");
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        return buffer;
    }

    // 64-bit layout: pointers align to 8, the flags word sits at 32, the BOOL at 36.
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    private struct ShFileOp
    {
        [FieldOffset(8)]
        public uint Function;

        [FieldOffset(16)]
        public IntPtr From;

        [FieldOffset(32)]
        public ushort Flags;

        [FieldOffset(36)]
        public int Aborted;
    }

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", ExactSpelling = true)]
    private static extern int SHFileOperation(ref ShFileOp operation);
}
