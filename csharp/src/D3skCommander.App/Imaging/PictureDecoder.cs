using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Imaging;

/// <summary>
/// Turns a picture file into a bitmap for the viewer and the thumbnails: TIFF through Core's
/// <see cref="TiffImage"/>, everything else through Avalonia's Skia decoder, chosen by content
/// (<see cref="PictureSignature"/>) rather than by name, then turned upright by its EXIF
/// orientation. It never throws: a file that does not decode, for whatever reason, is null.
/// </summary>
public static class PictureDecoder
{
    /// <summary>
    /// The picture at <paramref name="path"/>, scaled to <paramref name="width"/> pixels wide
    /// (before any quarter turn) when given; null when it does not decode.
    /// </summary>
    public static Bitmap? Decode(string path, int? width = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return PictureSignature.Of(path) == PictureFormat.Tiff ? DecodeTiff(path, width) : DecodeSkia(path, width);
        }
#pragma warning disable CA1031 // Any decoder failure (corrupt data, out of memory on a huge picture) is "no image", never a crash.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>A bitmap holding a copy of four-byte pixels (rows top to bottom, premultiplied alpha).</summary>
    internal static Bitmap FromRaster(RasterImage raster, PixelFormat? format = null)
    {
        var pinned = GCHandle.Alloc(raster.Rgba, GCHandleType.Pinned);
        try
        {
            return new Bitmap(
                format ?? PixelFormats.Rgba8888,
                AlphaFormat.Premul,
                pinned.AddrOfPinnedObject(),
                new PixelSize(raster.Width, raster.Height),
                new Vector(96, 96),
                raster.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
    }

    private static Bitmap? DecodeTiff(string path, int? width)
    {
        if (TiffImage.Decode(path) is not { } raster)
        {
            return null;
        }

        var bitmap = FromRaster(raster);
        if (width is not { } wanted || wanted >= raster.Width)
        {
            return bitmap;
        }

        using (bitmap)
        {
            var height = Math.Max(1, (int)Math.Round((double)raster.Height * wanted / raster.Width));
            return bitmap.CreateScaledBitmap(new PixelSize(wanted, height), BitmapInterpolationMode.MediumQuality);
        }
    }

    private static Bitmap DecodeSkia(string path, int? width)
    {
        Bitmap bitmap;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bitmap = width is { } wanted
                ? Bitmap.DecodeToWidth(stream, wanted, BitmapInterpolationMode.MediumQuality)
                : new Bitmap(stream);
        }

        var orientation = PictureOrientation.Read(path);
        return orientation == PictureOrientation.Upright ? bitmap : Turn(bitmap, orientation);
    }

    // Copies the decoded pixels out, turns them in Core and wraps them in a new bitmap.
    private static Bitmap Turn(Bitmap bitmap, int orientation)
    {
        using (bitmap)
        {
            var size = bitmap.PixelSize;
            var format = bitmap.Format ?? PixelFormats.Bgra8888;
            if (format.BitsPerPixel != 32)
            {
                throw new NotSupportedException("Only four-byte pixels are turned.");
            }

            var pixels = new byte[size.Width * size.Height * 4];
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), pixels.Length, size.Width * 4);
            }
            finally
            {
                pinned.Free();
            }

            var turned = PictureOrientation.Apply(new RasterImage(size.Width, size.Height, pixels), orientation);
            return FromRaster(turned, format);
        }
    }
}
