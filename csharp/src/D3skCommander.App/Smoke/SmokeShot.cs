using D3skCommander.App.Shell;

namespace D3skCommander.App.Smoke;

/// <summary>Writes the framebuffer, or a solid frame of the window size, as a PNG.</summary>
internal static class SmokeShot
{
    public static void Save(MainWindow window, string path)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var width = Pixels(window.Bounds.Width);
        var height = Pixels(window.Bounds.Height);
        var captured = Framebuffer.Read?.Invoke();
        if (captured is { } frame && frame.Width > 0 && frame.Height > 0)
        {
            width = frame.Width;
            height = frame.Height;
            PngWriter.Write(path, width, height, frame.Rgba, width * 4);
            return;
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 32;
            pixels[i + 1] = 32;
            pixels[i + 2] = 36;
            pixels[i + 3] = 255;
        }

        PngWriter.Write(path, width, height, pixels, stride);
    }

    private static int Pixels(double dips)
    {
        if (double.IsNaN(dips) || dips <= 0)
        {
            return 1;
        }

        var pixels = Math.Ceiling(dips);
        if (pixels >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)pixels;
    }
}
