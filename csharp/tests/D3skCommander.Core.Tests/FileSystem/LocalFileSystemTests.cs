using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.Core.Tests.FileSystem;

public class LocalFileSystemTests
{
    [Fact]
    public void NaturalOrderCountsNumbers()
    {
        var names = new List<string> { "file10", "File2", "file1", "a" };
        names.Sort(LocalFileSystem.CompareNatural);
        Assert.Equal(new[] { "a", "file1", "File2", "file10" }, names);
    }

    [Fact]
    public void NaturalOrderFoldsCaseBeyondAsciiAndNeverThrows()
    {
        var names = new List<string> { "😀", "été10", "b", "a1", "Été2", "a01" };
        names.Sort(LocalFileSystem.CompareNatural);
        Assert.Equal(new[] { "a01", "a1", "b", "Été2", "été10", "😀" }, names);
        Assert.True(LocalFileSystem.CompareNatural("Été", "été") < 0);
        Assert.True(LocalFileSystem.CompareNatural("a\uD800", "a") > 0);
        Assert.Equal(0, LocalFileSystem.CompareNatural("file007", "file007"));
    }

    [Fact]
    public void AListingReadsEachEntryAsAOnePathReadDoes()
    {
        using var dir = new TempFolder();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "12345");
        File.WriteAllText(Path.Combine(dir.Path, ".dot"), "");
        Directory.CreateDirectory(Path.Combine(dir.Path, "dir"));
        var readOnly = Path.Combine(dir.Path, "ro.txt");
        File.WriteAllText(readOnly, "x");
        MakeReadOnly(readOnly, true);
        TryLink(Path.Combine(dir.Path, "to-file"), Path.Combine(dir.Path, "a.txt"));
        TryLink(Path.Combine(dir.Path, "to-dir"), Path.Combine(dir.Path, "dir"));
        TryLink(Path.Combine(dir.Path, "broken"), Path.Combine(dir.Path, "missing"));
        try
        {
            var listed = ListOrFail(dir.Path, showHidden: true).Where(entry => !entry.IsParent).ToList();

            Assert.Contains(listed, entry => entry.Name == "ro.txt" && entry.ReadOnly);
            Assert.Contains(listed, entry => entry.Name == "a.txt" && entry.Size == 5);
            foreach (var entry in listed)
            {
                var alone = LocalFileSystem.ReadEntry(entry.Path);
                Assert.True(alone.IsSuccess, entry.Path);
                Assert.Equal(Fields(alone.Value), Fields(entry));
            }
        }
        finally
        {
            MakeReadOnly(readOnly, false);
        }
    }

    [Fact]
    public void SplitExtHandlesDotFiles()
    {
        Assert.Equal(new ExtensionParts(".bashrc", ""), LocalFileSystem.SplitExtension(".bashrc"));
        Assert.Equal(new ExtensionParts("a.tar", "gz"), LocalFileSystem.SplitExtension("a.tar.gz"));
        Assert.Equal(new ExtensionParts("README", ""), LocalFileSystem.SplitExtension("README"));
    }

    [Fact]
    public void ListSortAndHidden()
    {
        using var dir = new TempFolder();
        Directory.CreateDirectory(Path.Combine(dir.Path, "zdir"));
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "12345");
        File.WriteAllText(Path.Combine(dir.Path, "a.rs"), "1");
        File.WriteAllText(Path.Combine(dir.Path, ".hidden"), "");
        File.WriteAllText(Path.Combine(dir.Path, "été.md"), "");

        var hiddenOff = ListOrFail(dir.Path, showHidden: false);
        LocalFileSystem.Sort(hiddenOff, SortOrder.NameAscending);
        Assert.Equal(new[] { "..", "zdir", "a.rs", "b.txt", "été.md" }, Names(hiddenOff));

        var hiddenOn = ListOrFail(dir.Path, showHidden: true);
        LocalFileSystem.Sort(hiddenOn, new SortOrder(SortKey.Size, Descending: true));
        Assert.Equal("..", hiddenOn[0].Name);
        Assert.Equal("zdir", hiddenOn[1].Name);
        Assert.Equal("b.txt", hiddenOn[2].Name);
        Assert.Contains(".hidden", Names(hiddenOn));
    }

    [Fact]
    public void BranchViewListsNestedFiles()
    {
        using var dir = new TempFolder();
        Directory.CreateDirectory(Path.Combine(dir.Path, "a", "b"));
        File.WriteAllText(Path.Combine(dir.Path, "a", "b", "deep.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "top.txt"), "x");

        var entries = LocalFileSystem.ListBranch(dir.Path, showHidden: false, limit: 100, CancellationToken.None);
        LocalFileSystem.Sort(entries, SortOrder.NameAscending);
        var expected = Path.Combine("a", "b", "deep.txt");
        Assert.Equal(new[] { expected, "top.txt" }, Names(entries));
    }

    [Fact]
    public void DirSizeAndRename()
    {
        using var dir = new TempFolder();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "sub", "x"), "abc");
        File.WriteAllText(Path.Combine(dir.Path, "y"), "de");

        Assert.Equal(new FolderSize(5, 2, 1), LocalFileSystem.DirectorySize(dir.Path, CancellationToken.None));

        var renamed = LocalFileSystem.Rename(Path.Combine(dir.Path, "y"), "z");
        Assert.Null(renamed.Error);
        Assert.True(File.Exists(renamed.Value));

        var ontoDirectory = LocalFileSystem.Rename(renamed.Value, "sub");
        Assert.Equal(ErrorKind.AlreadyExists, ontoDirectory.Error?.Kind);
        Assert.Equal("a file with that name already exists", ontoDirectory.Error?.Message);

        var slashed = LocalFileSystem.Rename(renamed.Value, "a/b");
        Assert.Equal(ErrorKind.InvalidData, slashed.Error?.Kind);
        Assert.Equal("the name contains a forbidden character", slashed.Error?.Message);
    }

    [Fact]
    public void CreateFileRefusesExisting()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "new.txt");
        var created = LocalFileSystem.CreateFile(path);
        Assert.Null(created.Error);
        var again = LocalFileSystem.CreateFile(path);
        Assert.Equal(ErrorKind.AlreadyExists, again.Error?.Kind);
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("10 B", LocalFileSystem.FormatSize(10));
        Assert.Equal("1.5 KB", LocalFileSystem.FormatSize(1536));
        Assert.Equal("1 234 567", LocalFileSystem.FormatGrouped(1_234_567));
        Assert.Equal("12", LocalFileSystem.FormatGrouped(12));
        Assert.Equal("", LocalFileSystem.FormatTime(null));
    }

    [Fact]
    public void GvfsSharesAreListedByAddress()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "GVFS folder names hold ':', which Windows refuses.");

        using var dir = new TempFolder();
        var gvfs = Path.Combine(dir.Path, "gvfs");
        foreach (var name in new[] { "smb-share:server=nas,share=media", "sftp:host=box,user=me", "junk" })
        {
            Directory.CreateDirectory(Path.Combine(gvfs, name));
        }

        var labels = LocalFileSystem.GvfsDrives(gvfs).Select(share => share.Label).ToList();
        Assert.Equal(new[] { "sftp://me@box", "smb://nas/media" }, labels);
        Assert.Empty(LocalFileSystem.GvfsDrives(Path.Combine(dir.Path, "missing")));
    }

    private static List<Entry> ListOrFail(string directory, bool showHidden)
    {
        var listed = LocalFileSystem.ListDirectory(directory, showHidden);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        return listed.Value;
    }

    private static string Fields(Entry entry) =>
        string.Join('|', entry.Name, entry.Path, entry.IsDirectory, entry.IsLink, entry.Size, entry.Modified, entry.Hidden, entry.ReadOnly, entry.Attributes);

    private static void MakeReadOnly(string path, bool on)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, on ? FileAttributes.ReadOnly : FileAttributes.Normal);
            return;
        }

        File.SetUnixFileMode(path, on ? UnixFileMode.UserRead : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // Windows makes links only for an administrator or in developer mode: then the links are left out.
    private static void TryLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static List<string> Names(IEnumerable<Entry> entries) => entries.Select(entry => entry.Name).ToList();

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("d3sk-fs-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
