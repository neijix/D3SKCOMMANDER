using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace D3skCommander.App.Features.WindowsMenu;

// Win32 and COM declarations for the native Explorer menu. Only called behind OperatingSystem.IsWindows().
// DllImport: LibraryImport needs AllowUnsafeBlocks, which this project does not set.

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig]
    int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attributes);

    [PreserveSig]
    int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);

    [PreserveSig]
    int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

    [PreserveSig]
    int CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetAttributesOf(uint count, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref uint attributes);

    [PreserveSig]
    int GetUIObjectOf(IntPtr hwnd, uint count, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref Guid riid, IntPtr reserved, out IntPtr ppv);
}

[ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);

    [PreserveSig]
    int InvokeCommand(ref CmInvokeCommandInfoEx info);

    [PreserveSig]
    int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint size);
}

[ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2 : IContextMenu
{
    [PreserveSig]
    int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
}

[ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3 : IContextMenu2
{
    [PreserveSig]
    int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

/// <summary>CMINVOKECOMMANDINFOEX; the verb is an index made with MAKEINTRESOURCE, so the pointers hold small integers.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CmInvokeCommandInfoEx
{
    public uint Size;
    public uint Mask;
    public IntPtr Hwnd;
    public IntPtr Verb;
    public IntPtr Parameters;
    public IntPtr Directory;
    public int Show;
    public uint HotKey;
    public IntPtr Icon;
    public IntPtr Title;
    public IntPtr VerbW;
    public IntPtr ParametersW;
    public IntPtr DirectoryW;
    public IntPtr TitleW;
    public NativePoint InvokePoint;
}

internal delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WindowClassEx
{
    public uint Size;
    public uint Style;
    public WindowProc WindowProc;
    public int ClassExtra;
    public int WindowExtra;
    public IntPtr Instance;
    public IntPtr Icon;
    public IntPtr Cursor;
    public IntPtr Background;
    public string? MenuName;
    public string ClassName;
    public IntPtr SmallIcon;
}

internal static class ShellNative
{
    public const uint CmfNormal = 0;
    public const uint CmfExtendedVerbs = 0x100;
    public const uint TpmRightButton = 0x2;
    public const uint TpmReturnCommand = 0x100;
    public const uint CmicMaskUnicode = 0x4000;
    public const uint CmicMaskPointInvoke = 0x20000000;
    public const uint WmNull = 0x0000;
    public const uint WmDrawItem = 0x002B;
    public const uint WmMeasureItem = 0x002C;
    public const uint WmInitMenuPopup = 0x0117;
    public const uint WmMenuChar = 0x0120;
    public const uint WsPopup = 0x80000000;
    public const int SwShowNormal = 1;
    public const int VkShift = 0x10;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED): the user backed out of the command's own dialog.</summary>
    public const int HrCancelled = unchecked((int)0x800704C7);

    public static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    public static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr lastChild);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern short GetKeyState(int key);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);
}

/// <summary>
/// A hidden window that owns the native menu and hands the messages the shell extensions need
/// (WM_INITMENUPOPUP, WM_DRAWITEM, WM_MEASUREITEM, WM_MENUCHAR) to the menu's IContextMenu2/3, so submenus such as "Send to" fill and owner-drawn items paint.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MenuHostWindow : IDisposable
{
    private const string ClassName = "D3skCommanderShellMenuHost";
    private static readonly WindowProc Procedure = HandleMessage;
    private static bool _registered;

    // The menu is modal on the UI thread, so one at a time; the window procedure finds its menu here.
    private static IContextMenu? _active;

    private IntPtr _hwnd;

    private MenuHostWindow(IntPtr hwnd) => _hwnd = hwnd;

    public IntPtr Handle => _hwnd;

    /// <summary>Creates the hidden window, owned by <paramref name="owner"/> when it is a window. Null when Windows refuses.</summary>
    public static MenuHostWindow? Create(IntPtr owner)
    {
        var instance = ShellNative.GetModuleHandleW(null);
        if (!_registered)
        {
            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProc = Procedure,
                Instance = instance,
                ClassName = ClassName,
            };
            if (ShellNative.RegisterClassExW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                return null;
            }

            _registered = true;
        }

        var hwnd = ShellNative.CreateWindowExW(0, ClassName, null, ShellNative.WsPopup, 0, 0, 0, 0, owner, IntPtr.Zero, instance, IntPtr.Zero);
        return hwnd == IntPtr.Zero ? null : new MenuHostWindow(hwnd);
    }

    /// <summary>Routes the menu messages to <paramref name="menu"/> until <see cref="Dispose"/>.</summary>
    public void Attach(IContextMenu menu) => _active = menu;

    public void Dispose()
    {
        _active = null;
        if (_hwnd != IntPtr.Zero)
        {
            ShellNative.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private static IntPtr HandleMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (_active is { } menu
            && message is ShellNative.WmInitMenuPopup or ShellNative.WmDrawItem or ShellNative.WmMeasureItem or ShellNative.WmMenuChar
            && TryForward(menu, message, wParam, lParam, out var result))
        {
            return result;
        }

        return ShellNative.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static bool TryForward(IContextMenu menu, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        try
        {
            if (menu is IContextMenu3 menu3)
            {
                return menu3.HandleMenuMsg2(message, wParam, lParam, out result) >= 0;
            }

            if (menu is IContextMenu2 menu2 && menu2.HandleMenuMsg(message, wParam, lParam) >= 0)
            {
                // WM_INITMENUPOPUP answers 0; the owner-draw messages answer TRUE.
                result = message == ShellNative.WmInitMenuPopup ? IntPtr.Zero : 1;
                return true;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or InvalidCastException)
        {
            // A misbehaving shell extension must not take the window procedure down: leave the message to the default handler.
        }

        return false;
    }
}
