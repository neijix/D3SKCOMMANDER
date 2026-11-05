using System.Globalization;
using Avalonia.Media.Imaging;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Lister;

/// <summary>
/// Image mode of the viewer: decodes a file with Avalonia's <see cref="Bitmap"/> (Skia). Core only
/// decides which files qualify (<c>ViewerFiles.IsImage</c>); the file is read, never written.
/// </summary>
public static class ViewerImage
{
    /// <summary>The decoded image, or null when the file is corrupt, truncated or unreadable.</summary>
    public static Bitmap? Decode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The status line of an image: pixel size and file size.</summary>
    public static string Status(Bitmap image, ulong size) =>
        image.PixelSize.Width.ToString(CultureInfo.InvariantCulture) + " x "
        + image.PixelSize.Height.ToString(CultureInfo.InvariantCulture) + " | " + LocalFileSystem.FormatSize(size);
}
