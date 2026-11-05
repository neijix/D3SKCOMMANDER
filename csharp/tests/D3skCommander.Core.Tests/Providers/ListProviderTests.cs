using D3skCommander.Core.FileSystem;
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
