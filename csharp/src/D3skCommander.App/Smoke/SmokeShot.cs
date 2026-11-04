using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace D3skCommander.App.Smoke;

/// <summary>Renders one window to a PNG. Headless <c>Save</c> writes nothing, so the pixels are encoded then.</summary>
internal static class SmokeShot
{
    public static void Save(Window window, string path)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(path);
        window.UpdateLayout();
        var size = SizeOf(window);
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        TryPlatformSave(bitmap, path);
        if (IsPng(path))
        {
            return;
        }

        WritePixels(bitmap, path);
    }

    private static void TryPlatformSave(Bitmap bitmap, string path)
    {
        try
        {
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
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

        PngWriter.Write(path, size.Width, size.Height, pixels, stride);
    }

    private static PixelSize SizeOf(Window window)
    {
        var scale = window.RenderScaling;
        if (double.IsNaN(scale) || scale <= 0)
        {
            scale = 1;
        }

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

        if (width == 0)
        {
            width = 1;
        }

        if (height == 0)
        {
            height = 1;
        }

        return new PixelSize(width, height);
    }

    private static int Pixels(double dips, double scale)
    {
        if (double.IsNaN(dips) || dips <= 0)
        {
            return 0;
        }

        var pixels = dips * scale;
        if (pixels >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Ceiling(pixels);
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
