using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Providers;

public class ListProviderTests
{
    [Fact]
    public async Task BranchViewIsOneVirtualFolder()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-branch-").FullName;
        try
        {
            var nested = Path.Combine(root, "sub");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "deep.txt"), "x");
            File.WriteAllText(Path.Combine(root, "top.txt"), "y");
            var list = ListFileSystemProvider.Branch(root, showHidden: false, CancellationToken.None);
            var names = await Names(list, root);
            Assert.Contains(Path.Combine("sub", "deep.txt"), names);
            Assert.Contains("top.txt", names);
            Assert.DoesNotContain("sub", names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SearchResultsAreOneVirtualFolder()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-search-").FullName;
        try
        {
            var left = Path.Combine(root, "left");
            var right = Path.Combine(root, "right");
            Directory.CreateDirectory(left);
            Directory.CreateDirectory(right);
            var a = Path.Combine(left, "a.txt");
            var b = Path.Combine(right, "b.txt");
            File.WriteAllText(a, "a");
            File.WriteAllText(b, "b");
            var list = new ListFileSystemProvider("results", [Read(a), Read(b)]);
            var names = await Names(list, list.Directory);
            Assert.Equal(["a.txt", "b.txt"], names);
            var folder = await list.GetEntryAsync(list.Directory, CancellationToken.None);
            Assert.True(folder.IsSuccess, folder.Error?.Message);
            Assert.True(folder.Value.IsDirectory);
            var opened = await list.OpenReadAsync(a, CancellationToken.None);
            Assert.True(opened.IsSuccess, opened.Error?.Message);
            using var stream = opened.Value;
            Assert.Equal((byte)'a', stream.ReadByte());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HiddenRowsStayOutUntilAskedFor()
    {
        using var fixture = new ListProviderFixture();
        var shown = await Names(fixture.Provider, fixture.Directory, showHidden: false);
        var all = await Names(fixture.Provider, fixture.Directory, showHidden: true);
        Assert.DoesNotContain(".hidden", shown);
        Assert.Contains(".hidden", all);
    }

    [Fact]
    public async Task ResultsListSameNamedFilesFromTwoFoldersAndDropADeletedOne()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-results-").FullName;
        try
        {
            var one = Path.Combine(root, "one", "a.txt");
            var two = Path.Combine(root, "two", "a.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(one)!);
            Directory.CreateDirectory(Path.GetDirectoryName(two)!);
            File.WriteAllText(one, "1");
            File.WriteAllText(two, "22");
            var list = ListFileSystemProvider.Results(root, "Search: a*", [one, two, one, Path.Combine(root, "gone.txt")]);
            Assert.Equal(root, Path.GetDirectoryName(list.Directory));
            var names = await Names(list, list.Directory);
            Assert.Equal([Path.Combine("one", "a.txt"), Path.Combine("two", "a.txt")], names);

            File.Delete(one);
            File.WriteAllText(two, "333");
            var again = new List<Entry>();
            await foreach (var item in list.ListAsync(list.Directory, true, CancellationToken.None))
            {
                again.Add(item.Value);
            }

            var left = Assert.Single(again, entry => !entry.IsParent);
            Assert.Equal(two, left.Path);
            Assert.Equal(3UL, left.Size);
            Assert.Contains(again, entry => entry.IsParent && entry.Path == root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResultsKeepDuplicateGroupsTogetherAndSelectAllButFirstMarksTheExtraCopies()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-results-").FullName;
        try
        {
            var paths = new[] { "z1.txt", "a2.txt", "m1.txt", "b2.txt" }.Select(name => Path.Combine(root, name)).ToList();
            foreach (var path in paths)
            {
                File.WriteAllText(path, "x");
            }

            var list = ListFileSystemProvider.Results(root, "Search: dup", paths, [1, 2, 2, 1]);
            var tab = new Tab(new Location(list.Directory, list), SortOrder.NameAscending, list);
            await tab.ReloadAsync(true, TestContext.Current.CancellationToken);

            Assert.Equal(["b2.txt", "z1.txt", "a2.txt", "m1.txt"], tab.VisibleEntries.Where(e => !e.IsParent).Select(e => e.Name).ToList());
            tab.SelectAllButFirstOfGroups();
            Assert.Equal(["z1.txt", "m1.txt"], tab.Targets().Select(e => e.Name).ToList());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<List<string>> Names(IFileSystemProvider provider, string directory, bool showHidden = true)
    {
        var names = new List<string>();
        await foreach (var item in provider.ListAsync(directory, showHidden, CancellationToken.None))
        {
            Assert.True(item.IsSuccess, item.Error?.Message);
            if (!item.Value.IsParent)
            {
                names.Add(item.Value.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static Entry Read(string path)
    {
        var read = LocalFileSystem.ReadEntry(path);
        Assert.True(read.IsSuccess, read.Error?.Message);
        return read.Value;
    }
}
