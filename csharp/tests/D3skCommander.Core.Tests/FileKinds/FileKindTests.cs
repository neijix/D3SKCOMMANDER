using D3skCommander.Core.FileKinds;
using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.Core.Tests.FileKinds;

public class FileKindTests
{
    [Fact]
    public void KindsFollowTheExtensionCaseInsensitively()
    {
        Assert.Equal(FileKind.Image, File("a.PNG"));
        Assert.Equal(FileKind.Audio, File("song.flac"));
        Assert.Equal(FileKind.Video, File("clip.mkv"));
        Assert.Equal(FileKind.Pdf, File("report.pdf"));
        Assert.Equal(FileKind.Document, File("letter.docx"));
        Assert.Equal(FileKind.Spreadsheet, File("table.csv"));
        Assert.Equal(FileKind.Code, File("main.rs"));
        Assert.Equal(FileKind.Executable, File("setup.exe"));
        Assert.Equal(FileKind.Text, File("notes.md"));
        Assert.Equal(FileKind.Other, File("Makefile"));
        Assert.Equal(FileKind.Other, File(".bashrc"));
    }

    [Fact]
    public void ArchivesWinOverOtherKinds()
    {
        Assert.Equal(FileKind.Archive, File("src.tar.gz"));
        Assert.Equal(FileKind.Archive, File("a.zip"));
        Assert.Equal(FileKind.Archive, File("b.7z"));
    }

    [Fact]
    public void EntryFlagsComeFirst()
    {
        var path = Path.GetTempPath();
        var entry = Flagged(path, isDirectory: true, isLink: false, isParent: false);
        Assert.Equal(FileKind.Folder, FileKindClassifier.Of(entry));

        entry = Flagged(path, isDirectory: false, isLink: true, isParent: false);
        Assert.Equal(FileKind.Link, FileKindClassifier.Of(entry));

        entry = Flagged(path, isDirectory: false, isLink: true, isParent: true);
        Assert.Equal(FileKind.Parent, FileKindClassifier.Of(entry));
    }

    private static FileKind File(string name) => FileKindClassifier.OfFile(name);

    private static Entry Flagged(string path, bool isDirectory, bool isLink, bool isParent)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Length == 0)
        {
            name = "tmp";
        }

        return new Entry(name, path, isDirectory, isLink, isParent, 0, null, false, false, "");
    }
}
