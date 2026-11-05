using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * Scale(window), 96 * Scale(window)));
        bitmap.Render(window);
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
