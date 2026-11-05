using System.Text;
using D3skCommander.Core.Viewer;
using Xunit;

namespace D3skCommander.Core.Tests.Viewer;

/// <summary>The 12 tests of v0.1 <c>src/ui/lister/text.rs</c>.</summary>
public class ViewerTextTests
{
    [Fact]
    public void HexRowFormatsOffsetHexAndAscii()
    {
        var hello = "Hello, world!"u8;
        var chunk = new byte[16];
        hello.CopyTo(chunk);
        chunk[13] = 0x00;
        chunk[14] = 0x01;
        chunk[15] = 0xFF;
        var row = HexRow.Format(0x10, chunk);
        Assert.Equal(
            "00000010  48 65 6C 6C 6F 2C 20 77  6F 72 6C 64 21 00 01 FF  |Hello, world!...|",
            row);
    }

    [Fact]
    public void HexRowPadsAShortChunk()
    {
        var row = HexRow.Format(0, "AB"u8);
        Assert.StartsWith("00000000  41 42 ", row);
        Assert.EndsWith(" |AB|", row);
        Assert.Equal(HexRow.Format(0, new byte[16]).Length - 14, row.Length);
    }

    [Fact]
    public void DetectsBoms()
    {
        Assert.Equal(new ViewerEncodingMark(ViewerEncoding.Utf8Bom, 3), ViewerText.DetectEncoding(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)'b', (byte)'c' }));
        Assert.Equal(new ViewerEncodingMark(ViewerEncoding.Utf16Le, 2), ViewerText.DetectEncoding(new byte[] { 0xFF, 0xFE, (byte)'a', 0 }));
        Assert.Equal(new ViewerEncodingMark(ViewerEncoding.Utf16Be, 2), ViewerText.DetectEncoding(new byte[] { 0xFE, 0xFF, 0, (byte)'a' }));
        Assert.Equal(new ViewerEncodingMark(ViewerEncoding.Utf8, 0), ViewerText.DetectEncoding("plain"u8));
        Assert.Equal("UTF-8 BOM", ViewerEncodingNames.Name(ViewerEncoding.Utf8Bom));
        Assert.Equal("UTF-16 LE", ViewerEncodingNames.Name(ViewerEncoding.Utf16Le));
        Assert.Equal("UTF-16 BE", ViewerEncodingNames.Name(ViewerEncoding.Utf16Be));
        Assert.Equal("UTF-8", ViewerEncodingNames.Name(ViewerEncoding.Utf8));
    }

    [Fact]
    public void DecodesUtf16AndUtf8()
    {
        var le = ViewerText.Decode(new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0 });
        Assert.Equal("hi", le.Text);
        Assert.Equal(ViewerEncoding.Utf16Le, le.Encoding);

        var be = ViewerText.Decode(new byte[] { 0xFE, 0xFF, 0, (byte)'h', 0, (byte)'i' });
        Assert.Equal("hi", be.Text);
        Assert.Equal(ViewerEncoding.Utf16Be, be.Encoding);

        var bom = ViewerText.Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'c', (byte)'a', (byte)'f', 0xC3, 0xA9 });
        Assert.Equal("café", bom.Text);
        Assert.Equal(ViewerEncoding.Utf8Bom, bom.Encoding);

        Assert.Equal("a\uFFFDb", ViewerText.Decode(new byte[] { (byte)'a', 0xFF, (byte)'b' }).Text);
        Assert.Equal("a    b", ViewerText.Decode("a\tb"u8).Text);
    }

    [Fact]
    public void Utf16WithOddTrailingByteDoesNotPanic()
    {
        Assert.Equal("h", ViewerText.Decode(new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i' }).Text);
    }

    [Fact]
    public void BinaryDetection()
    {
        Assert.True(ViewerText.IsBinary("abc\0def"u8));
        Assert.False(ViewerText.IsBinary("just text\n"u8));
        Assert.False(ViewerText.IsBinary(new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0 }));

        var late = new byte[ViewerLimits.Sniff + 1];
        Array.Fill(late, (byte)'a');
        late[^1] = 0;
        Assert.False(ViewerText.IsBinary(late));
    }

    [Fact]
    public void SplitsLinesLikeLines()
    {
        const string text = "a\r\nbb\n\nccc";
        Assert.Equal(["a", "bb", "", "ccc"], Slices(text, ViewerText.LineRanges(text)));
        Assert.Empty(ViewerText.LineRanges(""));
        Assert.Single(ViewerText.LineRanges("x\n"));
    }

    [Fact]
    public void WrapsLongLines()
    {
        const string text = "abcdefg\nxy";
        var lines = ViewerText.LineRanges(text);
        var rows = ViewerText.WrapRows(text, lines, 3);
        Assert.Equal(
            [(0, "abc", true), (0, "def", false), (0, "g", false), (1, "xy", true)],
            rows.Select(row => (row.Line, text[row.Start..row.End], row.First)).ToArray());
    }

    [Fact]
    public void WrapHandlesMultibyteAndExactFit()
    {
        const string text = "ééé\nééééé";
        var lines = ViewerText.LineRanges(text);
        var rows = ViewerText.WrapRows(text, lines, 3);
        Assert.Equal(["ééé", "ééé", "éé"], rows.Select(row => text[row.Start..row.End]).ToArray());
    }

    [Fact]
    public void ClipsHugeLines()
    {
        var text = new string('x', ViewerLimits.MaxLineChars + 5);
        Assert.Equal(ViewerLimits.MaxLineChars + 2, ViewerText.ClipLine(text).EnumerateRunes().Count());
        Assert.Equal("short", ViewerText.ClipLine("short"));
    }

    [Fact]
    public void FindsLinesCaseInsensitivelyAndWraps()
    {
        const string text = "Alpha\nbeta\nALPHA beta\n";
        var lines = ViewerText.LineRanges(text);
        Assert.Equal(0, ViewerText.FindLine(text, lines, "alpha", false, 0, true));
        Assert.Equal(2, ViewerText.FindLine(text, lines, "alpha", false, 1, true));
        Assert.Equal(0, ViewerText.FindLine(text, lines, "alpha", false, 3, true));
        Assert.Null(ViewerText.FindLine(text, lines, "alpha", true, 0, true));
        Assert.Equal(1, ViewerText.FindLine(text, lines, "beta", true, 1, false));
        Assert.Equal(2, ViewerText.FindLine(text, lines, "beta", true, 0, false));
        Assert.Null(ViewerText.FindLine(text, lines, "", false, 0, true));
    }

    [Fact]
    public void FindsBytes()
    {
        var data = "xxAbcxxabc"u8.ToArray();
        Assert.Equal(7, ViewerText.FindBytes(data, "abc"u8, true, 0, true));
        Assert.Equal(2, ViewerText.FindBytes(data, "abc"u8, false, 0, true));
        Assert.Equal(7, ViewerText.FindBytes(data, "abc"u8, false, 3, true));
        Assert.Equal(2, ViewerText.FindBytes(data, "abc"u8, false, 8, true));
        Assert.Equal(2, ViewerText.FindBytes(data, "abc"u8, false, 6, false));
        Assert.Equal(7, ViewerText.FindBytes(data, "abc"u8, false, 1, false));
        Assert.Null(ViewerText.FindBytes(data, "zzz"u8, false, 0, true));
        Assert.Null(ViewerText.FindBytes("ab"u8, "abc"u8, false, 0, true));
    }

    private static string[] Slices(string text, List<LineSpan> lines)
    {
        var slices = new string[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var span = lines[i];
            slices[i] = text[span.Start..span.End];
        }

        return slices;
    }
}
