using BitMiracle.LibTiff.Classic;

namespace D3skCommander.Core.Viewer;

/// <summary>
/// Decoded pixels: <paramref name="Width"/> x <paramref name="Height"/>, rows top to bottom, four
/// bytes per pixel in R, G, B, A order with premultiplied alpha.
/// </summary>
public sealed record RasterImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Decodes the first page of a TIFF file with BitMiracle.LibTiff (managed, GUI-free), for the
/// viewer's Image mode, turned upright by its orientation tag. The file is only read. A corrupt, truncated or oversized file gives null.
/// </summary>
public static class TiffImage
{
    /// <summary>The largest image decoded (pixels): above it the four-byte buffer grows past 256 MB.</summary>
    public const long MaxPixels = 64L * 1024 * 1024;

    static TiffImage()
    {
        // LibTiff writes its warnings and errors to the console by default; a failure is a null here.
        Tiff.SetErrorHandler(new QuietHandler());
    }

    /// <summary>The first page of <paramref name="path"/>, or null when it does not decode.</summary>
    public static RasterImage? Decode(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var tiff = Tiff.ClientOpen(path, "r", stream, new TiffStream());
            return tiff is null ? null : Read(tiff);
        }
        catch (Exception exception) when (IsDecodeFailure(exception))
        {
            return null;
        }
    }

    private static RasterImage? Read(Tiff tiff)
    {
        var width = tiff.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt() ?? 0;
        var height = tiff.GetField(TiffTag.IMAGELENGTH)?[0].ToInt() ?? 0;
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
        {
            return null;
        }

        // LibTiff only flips; asking for the stored orientation keeps the rows as stored, and
        // PictureOrientation then applies all eight cases, the quarter turns included.
        var orientation = tiff.GetField(TiffTag.ORIENTATION)?[0].ToInt() ?? PictureOrientation.Upright;
        orientation = orientation is >= 1 and <= 8 ? orientation : PictureOrientation.Upright;
        var raster = new int[width * height];
        if (!tiff.ReadRGBAImageOriented(width, height, raster, (Orientation)orientation))
        {
            return null;
        }

        var rgba = new byte[raster.Length * 4];
        for (var i = 0; i < raster.Length; i++)
        {
            var pixel = raster[i];
            rgba[i * 4] = (byte)Tiff.GetR(pixel);
            rgba[(i * 4) + 1] = (byte)Tiff.GetG(pixel);
            rgba[(i * 4) + 2] = (byte)Tiff.GetB(pixel);
            rgba[(i * 4) + 3] = (byte)Tiff.GetA(pixel);
        }

        return PictureOrientation.Apply(new RasterImage(width, height, rgba), orientation);
    }

    // LibTiff reports malformed data with whatever its parser hits; none of these may reach the UI.
    private static bool IsDecodeFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or NotSupportedException or IndexOutOfRangeException or NullReferenceException or OverflowException
            or InvalidCastException or EndOfStreamException;

    private sealed class QuietHandler : TiffErrorHandler
    {
        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }

        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }
    }
}
