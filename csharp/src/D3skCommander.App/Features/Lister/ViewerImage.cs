using System.Globalization;
using Avalonia.Media.Imaging;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.Lister;

/// <summary>
/// Image mode of the viewer: decodes a file with Avalonia's <see cref="Bitmap"/> (Skia). Core only
/// decides which files qualify (<c>ViewerFiles.IsImage</c>); the file is read, never written.
/// </summary>
public static class ViewerImage
{
    /// <summary>Test seam: called with the path and bitmap of each successful <see cref="Open"/>.</summary>
    internal static Action<string, Bitmap>? Decoded { get; set; }

    /// <summary>
    /// Decodes the viewer's file; one that does not decode switches the viewer to bytes (hex) and returns null.
    /// </summary>
    public static Bitmap? Open(FileViewer viewer)
    {
        var image = Decode(viewer.Path);
        if (image is null)
        {
            viewer.UsePagedMode(ViewerMode.Hex);
            return null;
        }

        Decoded?.Invoke(viewer.Path, image);
        return image;
    }

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
