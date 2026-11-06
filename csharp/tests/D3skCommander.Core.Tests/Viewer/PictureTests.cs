using D3skCommander.Core.Viewer;
using Xunit;

namespace D3skCommander.Core.Tests.Viewer;

/// <summary>
/// Pictures recognised by content and turned upright. The turned* fixtures are 2 x 1 (red, blue)
/// carrying an EXIF orientation, written by Pillow: 6 shows red above blue, 8 blue above red.
/// </summary>
public class PictureTests
{
    private static readonly byte[] Red = [255, 0, 0, 255];
    private static readonly byte[] Blue = [0, 0, 255, 255];

    [Theory]
    [InlineData("pixel.png", PictureFormat.Png)]
    [InlineData("turned6.jpg", PictureFormat.Jpeg)]
    [InlineData("turned6.webp", PictureFormat.WebP)]
    [InlineData("turned6.tif", PictureFormat.Tiff)]
    [InlineData("pixel.tif", PictureFormat.Tiff)]
    [InlineData("missing.png", PictureFormat.Unknown)]
    public void AFormatIsKnownByItsFirstBytes(string name, PictureFormat format) =>
        Assert.Equal(format, PictureSignature.Of(Fixture(name)));

    [Fact]
    public void GifAndPlainBytesAreTold()
    {
        Assert.Equal(PictureFormat.Gif, PictureSignature.Of("GIF89a\x01\0\x01\0\0\0"u8));
        Assert.Equal(PictureFormat.Unknown, PictureSignature.Of("BM hello, plain text"u8));
        Assert.Equal(PictureFormat.Unknown, PictureSignature.Of("RIFF\0\0\0\0WAVE"u8));
        Assert.Equal(PictureFormat.Unknown, PictureSignature.Of([]));
    }

    [Theory]
    [InlineData("turned6.jpg", 6)]
    [InlineData("turned8.jpg", 8)]
    [InlineData("turned6.png", 6)]
    [InlineData("turned6.webp", 6)]
    [InlineData("pixel.png", 1)]
    [InlineData("corrupt.png", 1)]
    [InlineData("missing.jpg", 1)]
    public void TheExifOrientationIsReadFromJpegPngAndWebP(string name, int orientation) =>
        Assert.Equal(orientation, PictureOrientation.Read(Fixture(name)));

    [Fact]
    public void ABrokenExifBlockIsUpright()
    {
        Assert.Equal(1, PictureOrientation.FromTiff("Exif\0\0II*\0"u8));
        Assert.Equal(1, PictureOrientation.FromTiff("II*\0\xff\xff\xff\x7f"u8));
        Assert.Equal(1, PictureOrientation.FromTiff("XX*\0\x08\0\0\0\0\0"u8));
    }

    [Theory]
    [InlineData(1, 2, 1, "rb")]
    [InlineData(2, 2, 1, "br")]
    [InlineData(3, 2, 1, "br")]
    [InlineData(4, 2, 1, "rb")]
    [InlineData(5, 1, 2, "rb")]
    [InlineData(6, 1, 2, "rb")]
    [InlineData(7, 1, 2, "br")]
    [InlineData(8, 1, 2, "br")]
    public void EachOrientationTurnsThePixelsUpright(int orientation, int width, int height, string order)
    {
        var upright = PictureOrientation.Apply(new RasterImage(2, 1, [.. Red, .. Blue]), orientation);

        Assert.Equal((width, height), (upright.Width, upright.Height));
        Assert.Equal(order == "rb" ? Red : Blue, upright.Rgba[..4]);
        Assert.Equal(order == "rb" ? Blue : Red, upright.Rgba[4..8]);
    }

    [Fact]
    public void AQuarterTurnMovesEveryCornerOfALargerImage()
    {
        // 3 x 2, pixel value = index; orientation 6 (clockwise) gives 2 x 3: 3 0 / 4 1 / 5 2.
        var pixels = Enumerable.Range(0, 6).SelectMany(i => new byte[] { (byte)i, 0, 0, 0 }).ToArray();

        var turned = PictureOrientation.Apply(new RasterImage(3, 2, pixels), 6);

        Assert.Equal(new byte[] { 3, 0, 4, 1, 5, 2 }, turned.Rgba.Where((_, i) => i % 4 == 0).ToArray());
    }

    [Fact]
    public void ATiffIsTurnedByItsOwnOrientationTag()
    {
        var image = TiffImage.Decode(Fixture("turned6.tif"));

        Assert.NotNull(image);
        Assert.Equal((1, 2), (image.Width, image.Height));
        Assert.Equal(Red, image.Rgba[..4]);
        Assert.Equal(Blue, image.Rgba[4..8]);
    }

    [Fact]
    public void AMisnamedPictureStillOpensAsAnImage()
    {
        using var dir = new ViewerTemp();
        var path = Path.Combine(dir.Path, "photo.dat");
        File.Copy(Fixture("turned6.jpg"), path);

        Assert.Equal(ViewerMode.Image, FileViewer.Open(path).Mode);
    }

    [Theory]
    [InlineData("a.heic", true)]
    [InlineData("b.CR3", true)]
    [InlineData("c.svg", true)]
    [InlineData("d.jpg", false)]
    [InlineData("e.JFIF", false)]
    [InlineData("f.txt", false)]
    public void PicturesTheViewerCannotShowAreTold(string name, bool unsupported)
    {
        Assert.Equal(unsupported, ViewerFiles.IsUnsupportedPicture(name));
        Assert.Equal(!unsupported && name != "f.txt", ViewerFiles.IsImage(name));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "images", name);
}
