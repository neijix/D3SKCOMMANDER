using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Panels;

namespace D3skCommander.App.Smoke;

/// <summary>Renders one window to a PNG at its pixel size. Without a platform encoder the pixels are encoded here.</summary>
internal static class SmokeShot
{
    public static void Save(Window window, string path) => Save(window, path, static (bitmap, file) => bitmap.Save(file, PngBitmapEncoderOptions.Default));

    /// <summary>Test seam: <paramref name="platformSave"/> stands in for the platform PNG encoder.</summary>
    public static void Save(Window window, string path, Action<Bitmap, string> platformSave)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(platformSave);
        window.UpdateLayout();
        var size = SizeOf(window);
        var main = new RenderTargetBitmap(size, new Vector(96 * Scale(window), 96 * Scale(window)));
        main.Render(window);
        using var bitmap = WithOpenMenus(window, main);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Delete(path);
        TryPlatformSave(platformSave, bitmap, path);
        if (IsPng(path))
        {
            return;
        }

        WritePixels(bitmap, path);
    }

    // An open menu lives in its own popup window, which rendering the main window leaves out:
    // draw every open context menu at its screen position, growing the shot to hold a tall menu.
    private static RenderTargetBitmap WithOpenMenus(Window window, RenderTargetBitmap main)
    {
        var scale = Scale(window);
        var dpi = new Vector(96 * scale, 96 * scale);
        var popups = new List<(RenderTargetBitmap Shot, PixelPoint Origin)>();
        foreach (var menu in OpenMenus(window))
        {
            if (menu.Items.OfType<Visual>().FirstOrDefault() is not { } item || TopLevel.GetTopLevel(item) is not { } popup
                || ReferenceEquals(popup, window) || popup.Bounds.Width <= 0)
            {
                continue;
            }

            var size = new PixelSize(Math.Max(1, Pixels(popup.Bounds.Width, scale)), Math.Max(1, Pixels(popup.Bounds.Height, scale)));
            var shot = new RenderTargetBitmap(size, dpi);
            shot.Render(popup);
            var origin = popup.PointToScreen(default) - window.PointToScreen(default);
            popups.Add((shot, new PixelPoint(origin.X, origin.Y)));
        }

        if (popups.Count == 0)
        {
            return main;
        }

        var width = popups.Select(p => p.Origin.X + p.Shot.PixelSize.Width).Append(main.PixelSize.Width).Max();
        var height = popups.Select(p => p.Origin.Y + p.Shot.PixelSize.Height).Append(main.PixelSize.Height).Max();
        var combined = new RenderTargetBitmap(new PixelSize(width, height), dpi);
        using (var context = combined.CreateDrawingContext())
        {
            DrawPixels(context, main, new PixelPoint(0, 0));
            foreach (var (shot, origin) in popups)
            {
                DrawPixels(context, shot, origin);
                shot.Dispose();
            }
        }

        main.Dispose();
        return combined;
    }

    private static void DrawPixels(DrawingContext context, Bitmap bitmap, PixelPoint at)
    {
        var size = bitmap.PixelSize;
        context.DrawImage(bitmap, new Rect(0, 0, size.Width, size.Height), new Rect(at.X, at.Y, size.Width, size.Height));
    }

    private static IEnumerable<ContextMenu> OpenMenus(Window window)
    {
        foreach (var pane in window.GetVisualDescendants().OfType<PanelPaneView>())
        {
            if (pane.FileMenu is { IsOpen: true } menu)
            {
                yield return menu;
            }
        }
    }

    private static void TryPlatformSave(Action<Bitmap, string> platformSave, Bitmap bitmap, string path)
    {
        try
        {
            platformSave(bitmap, path);
        }
        catch (NotSupportedException)
        {
        }
        catch (NotImplementedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void WritePixels(Bitmap bitmap, string path)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), pin.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            pin.Free();
        }

        if (bitmap.Format == PixelFormats.Bgra8888)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }
        }

        PngWriter.Write(path, size.Width, size.Height, pixels, stride);
    }

    private static double Scale(Window window)
    {
        var scale = window.RenderScaling;
        return double.IsNaN(scale) || scale <= 0 ? 1 : scale;
    }

    private static PixelSize SizeOf(Window window)
    {
        var scale = Scale(window);
        var width = Pixels(window.Bounds.Width, scale);
        var height = Pixels(window.Bounds.Height, scale);
        if (width == 0)
        {
            width = Pixels(window.Width, scale);
        }

        if (height == 0)
        {
            height = Pixels(window.Height, scale);
        }

        return new PixelSize(Math.Max(width, 1), Math.Max(height, 1));
    }

    private static int Pixels(double dips, double scale)
    {
        if (double.IsNaN(dips) || dips <= 0)
        {
            return 0;
        }

        var pixels = dips * scale;
        return pixels >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(pixels);
    }

    private static bool IsPng(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var magic = new byte[8];
            if (stream.Read(magic, 0, magic.Length) != magic.Length)
            {
                return false;
            }

            return magic[0] == 137 && magic[1] == 80 && magic[2] == 78 && magic[3] == 71;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
