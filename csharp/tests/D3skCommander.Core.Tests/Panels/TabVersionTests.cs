using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class TabVersionTests
{
    [Fact]
    public async Task TheVersionMovesWithTheRowsNotWithTheCursorOrTheMarks()
    {
        var dir = Directory.CreateTempSubdirectory("d3sk-version-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            var tab = await Open(dir);
            var listed = tab.Version;

            tab.MoveCursor(1);
            tab.SelectAll();
            tab.InvertSelection();
            tab.Point(2);
            Assert.Equal(listed, tab.Version);

            tab.SetSort(SortKey.Size);
            var sorted = tab.Version;
            tab.SetFilter("a");
            var filtered = tab.Version;
            tab.SetFolderSize(Path.Combine(dir, "sub"), 42);
            var sized = tab.Version;
            await tab.ReloadAsync(false, TestContext.Current.CancellationToken);
            long[] seen = [listed, sorted, filtered, sized, tab.Version];
            Assert.Equal(seen.Length, seen.Distinct().Count());

            var twin = await Open(dir);
            Assert.DoesNotContain(twin.Version, seen);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static async Task<Tab> Open(string path)
    {
        var tab = new Tab(new Location(path), SortOrder.NameAscending, new LocalFileSystemProvider());
        await tab.ReloadAsync(false, TestContext.Current.CancellationToken);
        return tab;
    }
}
