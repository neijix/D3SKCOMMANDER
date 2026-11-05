using System.Text;
using D3skCommander.Core.TextDocument;
using Xunit;
using Document = D3skCommander.Core.TextDocument.TextDocument;

namespace D3skCommander.Core.Tests.TextDocument;

public class TextDocumentTests
{
    [Fact]
    public void DetectsLineEndings()
    {
        Assert.Equal(LineEnding.Crlf, TextCodec.DetectLineEnding("a\r\nb\r\n"));
        Assert.Equal(LineEnding.Lf, TextCodec.DetectLineEnding("a\nb\n"));
        Assert.Equal(LineEnding.Lf, TextCodec.DetectLineEnding("no newline"));
        Assert.Equal(LineEnding.Lf, TextCodec.DetectLineEnding("a\r\nb\nc\nd\n"));
    }

    [Fact]
    public void CrlfRoundTrip()
    {
        var src = "one\r\ntwo\r\n\r\nthree";
        var decoded = TextCodec.Decode(Encoding.UTF8.GetBytes(src));
        Assert.Equal("one\ntwo\n\nthree", decoded.Text);
        Assert.Equal(LineEnding.Crlf, decoded.LineEnding);
        Assert.Equal(Encoding.UTF8.GetBytes(src), TextCodec.Encode(decoded.Text, decoded.LineEnding, decoded.Bom));
    }

    [Fact]
    public void RestoreDoesNotDoubleCarriageReturns()
    {
        Assert.Equal("a\r\nb\r\nc", TextCodec.RestoreLineEnding("a\r\nb\nc", LineEnding.Crlf));
        Assert.Equal("a\nb", TextCodec.RestoreLineEnding("a\nb", LineEnding.Lf));
    }

    [Fact]
    public void BomAndInvalidUtf8()
    {
        var decoded = TextCodec.Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', (byte)'\n' });
        Assert.True(decoded.Bom);
        Assert.False(decoded.Lossy);
        Assert.Equal(
            new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', (byte)'\n' },
            TextCodec.Encode(decoded.Text, decoded.LineEnding, decoded.Bom));

        var bad = TextCodec.Decode(new byte[] { (byte)'a', 0xFF, (byte)'b' });
        Assert.True(bad.Lossy);
        Assert.Equal("a\uFFFDb", bad.Text);
    }

    [Fact]
    public void ReplaceAllCountsAndFoldsCase()
    {
        Assert.Equal(("aXa -", 1), TextSearch.ReplaceAll("aXa xa", "xa", "-", true));
        Assert.Equal(("a- -", 2), TextSearch.ReplaceAll("aXa xa", "xa", "-", false));
        Assert.Equal(("abc", 0), TextSearch.ReplaceAll("abc", "", "-", false));
        Assert.Equal(("e e", 2), TextSearch.ReplaceAll("é É", "é", "e", false));
        Assert.Equal(("ba", 1), TextSearch.ReplaceAll("aaa", "aa", "b", true));
    }

    [Fact]
    public void FindNextWrapsAround()
    {
        var text = "foo Bar foo";
        Assert.Equal(new Range(0, 3), TextSearch.FindNext(text, "foo", true, 0));
        Assert.Equal(new Range(8, 11), TextSearch.FindNext(text, "foo", true, 1));
        Assert.Equal(new Range(0, 3), TextSearch.FindNext(text, "foo", true, 9));
        Assert.Null(TextSearch.FindNext(text, "bar", true, 0));
        Assert.Equal(new Range(4, 7), TextSearch.FindNext(text, "bar", false, 0));
        Assert.Equal(new Range(3, 5), TextSearch.FindNext("é-é", "é", true, 1));
    }

    [Fact]
    public void AtomicWriteReplacesAndLeavesNoTemp()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "f.txt");
        File.WriteAllText(path, "old");
        var replaced = TextAtomicWrite.Write(path, "new"u8);
        Assert.True(replaced.IsSuccess, replaced.Error?.Message);
        Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(path));

        var fresh = Path.Combine(dir.Path, "fresh.txt");
        var created = TextAtomicWrite.Write(fresh, "x"u8);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal("x"u8.ToArray(), File.ReadAllBytes(fresh));
        Assert.Equal(2, Directory.GetFileSystemEntries(dir.Path).Length);
    }

    [Fact]
    public void NewEditorLoadsAndKeepsCrlfOnSave()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(path, "x\r\ny\r\n");
        var document = Document.Open(path);
        Assert.Equal("x\ny\n", document.Text);
        Assert.Equal(LineEnding.Crlf, document.LineEnding);
        document.Text += "z\n";
        Assert.True(document.Save());
        Assert.Equal(Encoding.UTF8.GetBytes("x\r\ny\r\nz\r\n"), File.ReadAllBytes(path));
        Assert.False(document.Dirty);
        Assert.Equal(TextNotice.Saved, document.Notice);
    }

    [Fact]
    public void MissingFileStartsEmptyAndSaveCreatesIt()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "new.txt");
        var document = Document.Open(path);
        Assert.Null(document.Refused);
        Assert.Equal("", document.Text);
        document.Text = "hi\n";
        Assert.True(document.Save());
        Assert.Equal(Encoding.UTF8.GetBytes("hi\n"), File.ReadAllBytes(path));
    }

    [Fact]
    public void OversizedFileIsRefused()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "big.bin");
        using (var file = File.Create(path))
        {
            file.SetLength(Document.MaxEditSize + 1);
        }

        var document = Document.Open(path);
        Assert.True(document.TooBig);
        Assert.NotNull(document.Refused);
        Assert.Equal(Document.TooBigMessage, document.Refused.Message);
        Assert.Equal("", document.Text);
    }

    [Fact]
    public void ReplaceEverywhereMarksDirty()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "r.txt");
        File.WriteAllText(path, "aa Aa");
        var document = Document.Open(path);
        document.Find = "aa";
        document.Replace = "b";
        document.ReplaceEverywhere();
        Assert.Equal("b b", document.Text);
        Assert.True(document.Dirty);
        Assert.Equal(TextNotice.Replaced, document.Notice);
        Assert.Equal(2, document.NoticeCount);
    }

    [Fact]
    public void FindAndReplaceSelects()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "e.txt");
        File.WriteAllText(path, "alpha beta\nBETA gamma\n");
        var document = Document.Open(path);
        document.Find = "beta";
        document.Replace = "X";
        document.FindNext();
        Assert.Equal(new Range(6, 10), document.PendingSelect);
        Assert.True(document.ApplyPendingSelect());
        document.ReplaceOne();
        Assert.Equal("alpha X\nBETA gamma\n", document.Text);
        Assert.True(document.Dirty);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("d3sk-text-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
