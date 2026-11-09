using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace D3skCommander.App.Features.WindowsMenu;

/// <summary>
/// The native Explorer menu through COM: SHParseDisplayName, SHBindToParent, GetUIObjectOf(IContextMenu),
/// QueryContextMenu, TrackPopupMenuEx, InvokeCommand. Off Windows it fails with a plain message.
/// </summary>
public sealed class WindowsExplorerMenu : IExplorerMenu
{
    public ExplorerMenuResult Show(IReadOnlyList<string> paths, PixelPoint? screenPoint, IntPtr ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!OperatingSystem.IsWindows())
        {
            return ExplorerMenuResult.Failed("Windows only");
        }

        try
        {
            using var session = ShellMenuSession.Open(paths, ownerWindow, out var error);
            if (session is null)
            {
                return ExplorerMenuResult.Failed(error ?? "unknown error");
            }

            return session.Track(screenPoint);
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or InvalidCastException or DllNotFoundException or EntryPointNotFoundException)
        {
            return ExplorerMenuResult.Failed(ex.Message);
        }
    }
}

/// <summary>
/// One native menu for one set of paths in one folder. Owns every COM object, the PIDLs, the HMENU and the hidden window:
/// <see cref="Dispose"/> releases them all, however the menu ended.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ShellMenuSession : IDisposable
{
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;

    private readonly List<IntPtr> _pidls = [];
    private readonly MenuHostWindow _host;
    private readonly string _folder;
    private object? _parentObject;
    private object? _menuObject;
    private IContextMenu? _menu;
    private IntPtr _hmenu;

    private ShellMenuSession(MenuHostWindow host, string folder)
    {
        _host = host;
        _folder = folder;
    }

    /// <summary>The number of top-level items the shell filled in (0 before a successful open).</summary>
    public int ItemCount => _hmenu == IntPtr.Zero ? 0 : ShellNative.GetMenuItemCount(_hmenu);

    /// <summary>Binds the paths and fills the menu. Null with <paramref name="error"/> when the shell refuses; nothing leaks then.</summary>
    public static ShellMenuSession? Open(IReadOnlyList<string> paths, IntPtr owner, out string? error)
    {
        error = null;
        if (paths.Count == 0)
        {
            error = "nothing to show";
            return null;
        }

        var full = paths.Select(Path.GetFullPath).ToList();
        var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full[0])) ?? full[0];
        if (full.Any(p => !string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(p)) ?? p, folder, StringComparison.OrdinalIgnoreCase)))
        {
            error = "the items are not all in one folder";
            return null;
        }

        var host = MenuHostWindow.Create(owner);
        if (host is null)
        {
            error = "could not create the menu window";
            return null;
        }

        var session = new ShellMenuSession(host, folder);
        error = session.Bind(full);
        if (error is not null)
        {
            session.Dispose();
            return null;
        }

        return session;
    }

    /// <summary>Shows the menu, waits, runs the chosen command. <paramref name="screenPoint"/> null = the mouse.</summary>
    public ExplorerMenuResult Track(PixelPoint? screenPoint)
    {
        int x;
        int y;
        if (screenPoint is { } point)
        {
            x = point.X;
            y = point.Y;
        }
        else
        {
            ShellNative.GetCursorPos(out var cursor);
            x = cursor.X;
            y = cursor.Y;
        }

        // The menu belongs to a window of this process that is in front, or Windows closes it on the next click elsewhere.
        ShellNative.SetForegroundWindow(_host.Handle);
        var command = ShellNative.TrackPopupMenuEx(_hmenu, ShellNative.TpmReturnCommand | ShellNative.TpmRightButton, x, y, _host.Handle, IntPtr.Zero);
        ShellNative.PostMessageW(_host.Handle, ShellNative.WmNull, IntPtr.Zero, IntPtr.Zero);
        return command == 0 ? ExplorerMenuResult.Dismissed : Invoke(command, x, y);
    }

    public void Dispose()
    {
        _host.Dispose();
        if (_hmenu != IntPtr.Zero)
        {
            ShellNative.DestroyMenu(_hmenu);
            _hmenu = IntPtr.Zero;
        }

        _menu = null;
        Release(ref _menuObject);
        foreach (var pidl in _pidls)
        {
            Marshal.FreeCoTaskMem(pidl);
        }

        _pidls.Clear();
        Release(ref _parentObject);
    }

    private static void Release(ref object? comObject)
    {
        if (comObject is not null)
        {
            Marshal.FinalReleaseComObject(comObject);
            comObject = null;
        }
    }

    private string? Bind(List<string> full)
    {
        foreach (var path in full)
        {
            var hr = ShellNative.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
            if (hr < 0)
            {
                return $"SHParseDisplayName 0x{hr:X8}: {path}";
            }

            _pidls.Add(pidl);
        }

        var children = new IntPtr[_pidls.Count];
        for (var i = 0; i < _pidls.Count; i++)
        {
            var iid = ShellNative.IidShellFolder;
            var hr = ShellNative.SHBindToParent(_pidls[i], ref iid, out var folderPointer, out children[i]);
            if (hr < 0)
            {
                return $"SHBindToParent 0x{hr:X8}";
            }

            if (i > 0)
            {
                Marshal.Release(folderPointer);
                continue;
            }

            _parentObject = Marshal.GetObjectForIUnknown(folderPointer);
            Marshal.Release(folderPointer);
        }

        var menuIid = ShellNative.IidContextMenu;
        var result = ((IShellFolder)_parentObject!).GetUIObjectOf(_host.Handle, (uint)children.Length, children, ref menuIid, IntPtr.Zero, out var menuPointer);
        if (result < 0 || menuPointer == IntPtr.Zero)
        {
            return $"GetUIObjectOf 0x{result:X8}";
        }

        _menuObject = Marshal.GetObjectForIUnknown(menuPointer);
        Marshal.Release(menuPointer);
        _menu = (IContextMenu)_menuObject;
        _host.Attach(_menu);

        _hmenu = ShellNative.CreatePopupMenu();
        if (_hmenu == IntPtr.Zero)
        {
            return "CreatePopupMenu failed";
        }

        var flags = ShellNative.CmfNormal | (ShellNative.GetKeyState(ShellNative.VkShift) < 0 ? ShellNative.CmfExtendedVerbs : 0);
        result = _menu.QueryContextMenu(_hmenu, 0, FirstCommand, LastCommand, flags);
        return result < 0 ? $"QueryContextMenu 0x{result:X8}" : null;
    }

    private ExplorerMenuResult Invoke(uint command, int x, int y)
    {
        // The verb is the command's index, passed as MAKEINTRESOURCE (a pointer holding a small integer).
        var verb = (IntPtr)(command - FirstCommand);
        var directory = Marshal.StringToCoTaskMemUni(_folder);
        try
        {
            var info = new CmInvokeCommandInfoEx
            {
                Size = (uint)Marshal.SizeOf<CmInvokeCommandInfoEx>(),
                Mask = ShellNative.CmicMaskUnicode | ShellNative.CmicMaskPointInvoke,
                Hwnd = _host.Handle,
                Verb = verb,
                VerbW = verb,
                DirectoryW = directory,
                Show = ShellNative.SwShowNormal,
                InvokePoint = new NativePoint { X = x, Y = y },
            };
            var hr = _menu!.InvokeCommand(ref info);
            if (hr >= 0)
            {
                return ExplorerMenuResult.Invoked;
            }

            return hr == ShellNative.HrCancelled
                ? ExplorerMenuResult.Dismissed
                : ExplorerMenuResult.Failed($"InvokeCommand 0x{hr:X8}");
        }
        finally
        {
            Marshal.FreeCoTaskMem(directory);
        }
    }
}
