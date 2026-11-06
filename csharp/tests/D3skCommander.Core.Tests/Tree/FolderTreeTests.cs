using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Tree;
using Xunit;

namespace D3skCommander.Core.Tests.Tree;

public class FolderTreeTests
{
    [Fact]
    public void RootForPicksTheLongestRootThatHoldsThePath()
    {
        string[] roots = ["/", "/home/ann", "/home/ann/mnt", "/home/anne"];

        Assert.Equal("/home/ann/mnt", FolderTree.RootFor("/home/ann/mnt/x/y", roots));
        Assert.Equal("/home/ann", FolderTree.RootFor("/home/ann/docs", roots));
        Assert.Equal("/home/ann", FolderTree.RootFor("/home/ann", roots));
        Assert.Equal("/home/anne", FolderTree.RootFor("/home/anne/x", roots));
        Assert.Equal("/", FolderTree.RootFor("/usr/lib", roots));
        Assert.Null(FolderTree.RootFor("/usr/lib", ["/home/ann"]));
        Assert.Null(FolderTree.RootFor("/home/annex", ["/home/ann"]));
    }

    [Fact]
    public void RootForUnderstandsWindowsStylePaths()
    {
        string[] roots = ["C:\\Users\\Ann", "C:\\", "D:\\"];

        Assert.Equal("C:\\Users\\Ann", FolderTree.RootFor("C:\\Users\\Ann\\Docs", roots));
        Assert.Equal("C:\\Users\\Ann", FolderTree.RootFor("c:\\users\\ann\\docs", roots));
        Assert.Equal("C:\\", FolderTree.RootFor("C:\\Windows", roots));
        Assert.Equal("D:\\", FolderTree.RootFor("D:\\", roots));
        Assert.Null(FolderTree.RootFor("E:\\x", roots));
        Assert.Null(FolderTree.RootFor("C:\\Users\\Anna", ["C:\\Users\\Ann"]));
    }

    [Fact]
    public void ChainFromListsEveryFolderFromTheRootDown()
    {
        Assert.Equal(["/home/ann", "/home/ann/a", "/home/ann/a/b"], FolderTree.ChainFrom("/home/ann", "/home/ann/a/b/"));
        Assert.Equal(["/", "/usr", "/usr/lib"], FolderTree.ChainFrom("/", "/usr/lib"));
        Assert.Equal(["/home/ann"], FolderTree.ChainFrom("/home/ann", "/home/ann"));
        Assert.Equal(["C:\\", "C:\\Users", "C:\\Users\\Ann"], FolderTree.ChainFrom("C:\\", "C:\\Users\\Ann"));
        Assert.Empty(FolderTree.ChainFrom("/home/ann", "/home/bob/x"));
    }

    [Fact]
    public void ChildFoldersKeepFoldersOnlySortedAndFilterHiddenOnes()
    {
        var listing = new[]
        {
            Item("zeta", true),
            Item("Alpha", true),
            Item("beta", true),
            Item("notes.txt", false),
            Item(".cache", true, hidden: true),
            Item("..", true, parent: true),
        };

        Assert.Equal(["Alpha", "beta", "zeta"], FolderTree.ChildFolders(listing, false).Select(f => f.Name));
        Assert.Equal([".cache", "Alpha", "beta", "zeta"], FolderTree.ChildFolders(listing, true).Select(f => f.Name));
    }

    [Fact]
    public async Task ALoaderReadsTheFoldersOfATempTree()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-tree-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "b"));
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, ".hid"));
            await File.WriteAllTextAsync(Path.Combine(root, "file.txt"), "x", TestContext.Current.CancellationToken);
            var loader = new FolderTreeLoader(new LocalFileSystemProvider());

            var plain = await loader.LoadChildrenAsync(root, false, TestContext.Current.CancellationToken);
            var all = await loader.LoadChildrenAsync(root, true, TestContext.Current.CancellationToken);

            Assert.False(plain.Failed);
            Assert.Equal(["A", "b"], plain.Folders.Select(f => f.Name));
            Assert.Equal(Path.Combine(root, "A"), plain.Folders[0].Path);
            Assert.Equal([".hid", "A", "b"], all.Folders.Select(f => f.Name));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AFolderThatCannotBeReadGivesAnEmptyFailedResult()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-tree-").FullName;
        var locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        var loader = new FolderTreeLoader(new LocalFileSystemProvider());
        try
        {
            var missing = await loader.LoadChildrenAsync(Path.Combine(root, "gone"), false, TestContext.Current.CancellationToken);
            Assert.True(missing.Failed);
            Assert.Empty(missing.Folders);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(locked, UnixFileMode.None);
                var denied = await loader.LoadChildrenAsync(locked, false, TestContext.Current.CancellationToken);
                Assert.Empty(denied.Folders);
            }
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(root, true);
        }
    }

    private static Entry Item(string name, bool directory, bool hidden = false, bool parent = false) =>
        new(name, "/r/" + name, directory, false, parent, 0, null, hidden, false, "");
}
