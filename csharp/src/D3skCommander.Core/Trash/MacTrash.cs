using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Trash;

/// <summary><c>NSFileManager trashItemAtURL:resultingItemURL:error:</c> through the Objective-C runtime.</summary>
[SupportedOSPlatform("osx")]
internal static class MacTrash
{
    public static TrashStatus Move(string path)
    {
        var pool = IntPtr.Zero;
        try
        {
            pool = Send(Class("NSAutoreleasePool"), Sel("new"));
            var nsPath = SendUtf8(Class("NSString"), Sel("stringWithUTF8String:"), path);
            var url = SendOne(Class("NSURL"), Sel("fileURLWithPath:"), nsPath);
            var manager = Send(Class("NSFileManager"), Sel("defaultManager"));
            var ok = SendTrash(
                manager,
                Sel("trashItemAtURL:resultingItemURL:error:"),
                url,
                IntPtr.Zero,
                out var error);
            if (ok != 0 && error == IntPtr.Zero)
            {
                return TrashStatus.Success;
            }

            var message = Describe(error);
            return TrashStatus.Fail(OperationError.FromMessage(message ?? "trash failed", path));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return TrashStatus.Unavailable(path);
        }
        finally
        {
            if (pool != IntPtr.Zero)
            {
                Send(pool, Sel("drain"));
            }
        }
    }

    private static string? Describe(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return null;
        }

        var description = Send(error, Sel("localizedDescription"));
        var utf8 = Send(description, Sel("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    private static IntPtr Class(string name) => GetClass(name);

    private static IntPtr Sel(string name) => RegisterName(name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendOne(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendUtf8(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    // BOOL is a signed char. The last argument is NSError**.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte SendTrash(
        IntPtr receiver,
        IntPtr selector,
        IntPtr url,
        IntPtr resultingItem,
        out IntPtr error);
}
