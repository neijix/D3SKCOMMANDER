using D3skCommander.Core.Viewer;

namespace D3skCommander.Core.Preview;

/// <summary>The picture a camera RAW file carries for previews: its embedded JPEG and the way up.</summary>
public sealed record CameraRawPreview(ReadOnlyMemory<byte> Jpeg, int Orientation);

/// <summary>Reads the preview of a camera RAW file (see <see cref="EmbeddedJpeg"/>). Never throws.</summary>
public static class CameraRaw
{
    /// <summary>Larger files are not read whole for a preview.</summary>
    public const long MaxBytes = 400L * 1024 * 1024;

    /// <summary>The preview of the RAW file at <paramref name="path"/>; null when it holds none or cannot be read.</summary>
    public static CameraRawPreview? Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxBytes ? Of(File.ReadAllBytes(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The preview inside <paramref name="data"/>; TIFF-based files (CR2, NEF, ARW, DNG…) give the way up.</summary>
    public static CameraRawPreview? Of(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var jpeg = EmbeddedJpeg.Largest(data);
        if (jpeg.IsEmpty)
        {
            return null;
        }

        var head = data.AsSpan(0, Math.Min(data.Length, PictureSignature.Length));
        var tiff = PictureSignature.Of(head) == PictureFormat.Tiff;
        return new CameraRawPreview(jpeg, tiff ? PictureOrientation.FromTiff(data) : PictureOrientation.Upright);
    }
}
