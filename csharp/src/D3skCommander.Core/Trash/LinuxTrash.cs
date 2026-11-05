using System.Runtime.InteropServices;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Trash;

/// <summary><c>g_file_trash</c> from libgio-2.0. No library means trash is unavailable.</summary>
internal static class LinuxTrash
{
    // G_IO_ERROR_NOT_FOUND, PERMISSION_DENIED, NOT_SUPPORTED.
    private const int NotFound = 1;
    private const int PermissionDenied = 14;
    private const int NotSupported = 15;

    private static readonly bool Ready = Probe();

    public static TrashStatus Move(string path)
    {
        if (!Ready)
        {
            return TrashStatus.Unavailable(path);
        }

        var file = IntPtr.Zero;
        var error = IntPtr.Zero;
        try
        {
            file = NewForPath(path);
            if (file == IntPtr.Zero)
            {
                return TrashStatus.Fail(OperationError.InvalidData("not a path", path));
            }

            var ok = TrashFile(file, IntPtr.Zero, ref error);
            if (ok != 0 && error == IntPtr.Zero)
            {
                return TrashStatus.Success;
            }

            return FromGError(error, path);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return TrashStatus.Unavailable(path);
        }
        finally
        {
            if (error != IntPtr.Zero)
            {
                FreeError(error);
            }

            if (file != IntPtr.Zero)
            {
                Unref(file);
            }
        }
    }

    private static TrashStatus FromGError(IntPtr error, string path)
    {
        if (error == IntPtr.Zero)
        {
            return TrashStatus.Fail(OperationError.Other("trash failed", path));
        }

        var code = Marshal.ReadInt32(error, 4);
        var message = ReadMessage(error);
        if (code == NotSupported || IsUnavailableText(message))
        {
            return TrashStatus.Unavailable(path);
        }

        if (code == NotFound)
        {
            return TrashStatus.Fail(OperationError.NotFound(path, message));
        }

        if (code == PermissionDenied)
        {
            return TrashStatus.Fail(OperationError.AccessDenied(message ?? "permission denied", path));
        }

        return TrashStatus.Fail(OperationError.FromMessage(message ?? "trash failed", path));
    }

    private static bool IsUnavailableText(string? message) =>
        message is not null && message.Contains("not supported", StringComparison.OrdinalIgnoreCase);

    // domain (4) + code (4), then the message pointer. Same on 32-bit and 64-bit.
    private static string? ReadMessage(IntPtr error)
    {
        var message = Marshal.ReadIntPtr(error, 8);
        return message == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(message);
    }

    private static bool Probe()
    {
        if (!NativeLibrary.TryLoad("libgio-2.0.so.0", out var gio))
        {
            return false;
        }

        var exports = NativeLibrary.TryGetExport(gio, "g_file_trash", out _)
            && NativeLibrary.TryGetExport(gio, "g_file_new_for_path", out _);
        return exports
            && NativeLibrary.TryLoad("libgobject-2.0.so.0", out _)
            && NativeLibrary.TryLoad("libglib-2.0.so.0", out _);
    }

    // gboolean is a 4-byte int, not a C bool.
    [DllImport("libgio-2.0.so.0", EntryPoint = "g_file_new_for_path")]
    private static extern IntPtr NewForPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport("libgio-2.0.so.0", EntryPoint = "g_file_trash")]
    private static extern int TrashFile(IntPtr file, IntPtr cancellable, ref IntPtr error);

    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    private static extern void Unref(IntPtr obj);

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
    private static extern void FreeError(IntPtr error);
}
