using System.Text;
using D3skCommander.Core.Preview;
using Xunit;

namespace D3skCommander.Core.Tests.Preview;

public class PreviewTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "images", name);

    [Theory]
    [InlineData("a.pdf", "%PDF-1.4", PreviewKind.Pdf)]
    [InlineData("noname", "%PDF-1.7", PreviewKind.Pdf)]
    [InlineData("shot.CR2", "II*\0", PreviewKind.CameraRaw)]
    [InlineData("shot.nef", "MM\0*", PreviewKind.CameraRaw)]
    [InlineData("shot.bmp", "BM", PreviewKind.Picture)]
    [InlineData("misnamed.dat", "GIF89a", PreviewKind.Picture)]
    [InlineData("pack.zip", "PK\u0003\u0004", PreviewKind.Archive)]
    [InlineData("pack.7z", "7z", PreviewKind.Archive)]
    [InlineData("readme", "hello", PreviewKind.Text)]
    [InlineData("Program.cs", "class A {}", PreviewKind.Text)]
    [InlineData("empty.txt", "", PreviewKind.Text)]
    [InlineData("tool.exe", "MZ\0\0", PreviewKind.Other)]
    [InlineData("song.mp3", "ID3", PreviewKind.Audio)]
    [InlineData("take.FLAC", "fLaC", PreviewKind.Audio)]
    [InlineData("voice.wav", "RIFF\0\0\0\0WAVEfmt ", PreviewKind.Audio)]
    public void TheKindComesFromTheNameAndTheFirstBytes(string name, string head, PreviewKind expected)
    {
        Assert.Equal(expected, PreviewKinds.Of(name, head.Length, Encoding.Latin1.GetBytes(head)));
    }

    [Fact]
    public void LargeOrUtf16TextIsJudgedByItsSizeAndMark()
    {
        Assert.Equal(PreviewKind.Other, PreviewKinds.Of("big.log", PreviewKinds.TextLimit + 1, "abc"u8));
        Assert.Equal(PreviewKind.Text, PreviewKinds.Of("wide.txt", 6, [0xFF, 0xFE, (byte)'a', 0, (byte)'b', 0]));
    }

    [Fact]
    public void TextDecodesItsMarkUtf8OrLatin1()
    {
        Assert.Equal("é", TextPreview.Decode([0xEF, 0xBB, 0xBF, 0xC3, 0xA9]));
        Assert.Equal("é", TextPreview.Decode([0xC3, 0xA9]));
        Assert.Equal("é", TextPreview.Decode([0xE9]));
        Assert.Equal("ab", TextPreview.Decode([0xFF, 0xFE, (byte)'a', 0, (byte)'b', 0]));
    }

    [Fact]
    public void TheLargestEmbeddedJpegIsFoundWholeAndStrayMarksAreIgnored()
    {
        var photo = File.ReadAllBytes(Fixture("turned6.jpg"));
        var small = Jpeg(0xC0, 4);
        byte[] stray = [0xFF, 0xD8, 0xFF, 0x12, 0x34];
        var data = Concat([0x00, 0x01], small, stray, photo, [0xAA, 0xBB]);

        var found = EmbeddedJpeg.Largest(data);

        Assert.Equal(photo, found.ToArray());
    }

    [Fact]
    public void LosslessJpegIsSkippedAndNothingFoundIsEmpty()
    {
        var lossless = Jpeg(0xC3, 64);
        var baseline = Jpeg(0xC0, 4);

        Assert.Equal(baseline, EmbeddedJpeg.Largest(Concat(lossless, baseline)).ToArray());
        Assert.True(EmbeddedJpeg.Largest([1, 2, 3, 0xFF, 0xD8]).IsEmpty);
    }

    [Fact]
    public void ARawFileGivesItsPreviewAndTheWayUpFromItsTiffHeader()
    {
        var photo = File.ReadAllBytes(Fixture("turned6.jpg"));
        byte[] tiff =
        [
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            1, 0,
            0x12, 0x01, 3, 0, 1, 0, 0, 0, 8, 0, 0, 0,
            0, 0, 0, 0,
        ];

        var preview = CameraRaw.Of(Concat(tiff, new byte[100], photo));

        Assert.NotNull(preview);
        Assert.Equal(8, preview.Orientation);
        Assert.Equal(photo, preview.Jpeg.ToArray());
        Assert.Null(CameraRaw.Of(tiff));
    }

    [Fact]
    public void APdfPageIsDrawnOnWhiteAtTheAskedWidth()
    {
        using var pdf = PdfPages.Load(OnePagePdf(200, 100));

        Assert.NotNull(pdf);
        Assert.Equal(1, pdf.Count);
        var page = pdf.Render(0, 100);
        Assert.NotNull(page);
        Assert.Equal(100, page.Width);
        Assert.Equal(50, page.Height);
        Assert.Equal(0xFF, page.Rgba[0]);
        Assert.Null(pdf.Render(1, 100));
    }

    [Fact]
    public void BytesThatAreNotAPdfDoNotLoad()
    {
        Assert.Null(PdfPages.Load("not a pdf"u8.ToArray()));
    }

    // A tiny JPEG skeleton: SOI, a frame of the given type, a scan with a stuffed FF 00 byte, EOI.
    private static byte[] Jpeg(byte frame, int scanBytes)
    {
        var scan = Enumerable.Repeat((byte)0x11, scanBytes).Concat(new byte[] { 0xFF, 0x00 }).ToArray();
        return Concat(
            [0xFF, 0xD8],
            [0xFF, frame, 0x00, 0x0B, 8, 0, 1, 0, 1, 1, 1, 0x11, 0],
            [0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0],
            scan,
            [0xFF, 0xD9]);
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static byte[] OnePagePdf(int width, int height)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] >>",
        };
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(text.Length);
            text.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xref = text.Length;
        text.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            text.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        text.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
