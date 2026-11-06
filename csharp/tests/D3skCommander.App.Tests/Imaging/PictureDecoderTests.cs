using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using D3skCommander.App.Imaging;
using Xunit;

namespace D3skCommander.App.Tests.Imaging;

/// <summary>
/// The turned* fixtures are 2 x 1 (red, blue) with an EXIF orientation: 6 shows red above blue,
/// 8 blue above red. Every format decodes upright; nothing that fails to decode throws.
/// </summary>
public sealed class PictureDecoderTests
{
    [AvaloniaTheory]
    [InlineData("turned6.jpg", true)]
    [InlineData("turned8.jpg", false)]
    [InlineData("turned6.png", true)]
    [InlineData("turned6.webp", true)]
    [InlineData("turned6.tif", true)]
    public void APhotoIsShownUprightWhateverItsFormat(string name, bool redOnTop)
    {
        using var bitmap = PictureDecoder.Decode(Fixture(name));

        Assert.NotNull(bitmap);
        Assert.Equal(new PixelSize(1, 2), bitmap.PixelSize);
        Assert.Equal(redOnTop, IsRed(Pixel(bitmap, 0)));
        Assert.Equal(!redOnTop, IsRed(Pixel(bitmap, 1)));
    }

    [AvaloniaFact]
    public void AThumbnailIsScaledAndTurnedToo()
    {
        using var tiff = PictureDecoder.Decode(Fixture("pixel.tif"), 2);
        using var jpeg = PictureDecoder.Decode(Fixture("turned6.jpg"), 2);

        Assert.Equal(2, tiff?.PixelSize.Width);
        Assert.Equal(new PixelSize(1, 2), jpeg?.PixelSize);
    }

    [AvaloniaFact]
    public void BrokenMissingOrMisleadingFilesGiveNoImageInsteadOfThrowing()
    {
        var dir = Directory.CreateTempSubdirectory("d3sk-picture-").FullName;
        try
        {
            AssertNothingDecodes(dir);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void AssertNothingDecodes(string dir)
    {
        var fake = Path.Combine(dir, "fake.jpg");
        File.WriteAllBytes(fake, [0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 1, 2, 3]);
        var empty = Path.Combine(dir, "empty.png");
        File.WriteAllBytes(empty, []);

        Assert.Null(PictureDecoder.Decode(fake));
        Assert.Null(PictureDecoder.Decode(empty));
        Assert.Null(PictureDecoder.Decode(Fixture("corrupt.png")));
        Assert.Null(PictureDecoder.Decode(Fixture("corrupt.tif"), 64));
        Assert.Null(PictureDecoder.Decode(Path.Combine(dir, "missing.webp")));
        Assert.Null(PictureDecoder.Decode(dir));
    }

    private static bool IsRed((byte R, byte G, byte B) pixel) => pixel.R > 180 && pixel.B < 80;

    private static (byte R, byte G, byte B) Pixel(Bitmap bitmap, int row)
    {
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4);
        }
        finally
        {
            pinned.Free();
        }

        var at = row * bitmap.PixelSize.Width * 4;
        return bitmap.Format == PixelFormats.Rgba8888
            ? (pixels[at], pixels[at + 1], pixels[at + 2])
            : (pixels[at + 2], pixels[at + 1], pixels[at]);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "images", name);
}
