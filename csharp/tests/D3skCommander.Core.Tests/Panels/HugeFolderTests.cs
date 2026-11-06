using System.Diagnostics;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Testing;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

/// <summary>
/// A 100k-entry folder: listing, sort, filter and selection stay within budgets several times
/// what a laptop measures (list ~0.4 s, a sort ~70 ms, a filter ~40 ms), so a slower machine passes.
/// </summary>
public class HugeFolderTests
{
    private const int Count = 100_000;

    [Fact]
    [Slow]
    public async Task AHundredThousandEntryFolderListsSortsFiltersAndSelectsWithinBudget()
    {
        var dir = Directory.CreateTempSubdirectory("d3sk-huge-").FullName;
        try
        {
            for (var i = 0; i < Count; i++)
            {
                File.Create(Path.Combine(dir, "file" + i + (i % 3 == 0 ? ".txt" : ".dat"))).Dispose();
            }

            var tab = new Tab(new Location(dir), SortOrder.NameAscending, new LocalFileSystemProvider());
            await Within(TimeSpan.FromSeconds(3), () => tab.ReloadAsync(false, TestContext.Current.CancellationToken));
            Assert.Equal(Count + 1, tab.VisibleEntries.Count());
            Assert.Equal(["..", "file0.txt", "file1.dat", "file2.dat"], tab.VisibleEntries.Take(4).Select(e => e.Name));

            foreach (var key in new[] { SortKey.Extension, SortKey.Size, SortKey.Modified, SortKey.Name })
            {
                Within(TimeSpan.FromMilliseconds(600), () => tab.SetSort(key));
            }

            Assert.Equal("file0.txt", tab.VisibleEntries.ElementAt(1).Name);
            Within(TimeSpan.FromMilliseconds(600), () => tab.SetSort(SortKey.Name));
            Assert.Equal("file99999.txt", tab.VisibleEntries.ElementAt(1).Name);
            Within(TimeSpan.FromMilliseconds(300), () => tab.SetFilter("9999"));
            Assert.Equal(19, tab.VisibleEntries.Count(e => !e.IsParent)); // x9999 for x = 0..9, then 99990..99998
            Within(TimeSpan.FromMilliseconds(100), () => tab.SetFilter(null));
            Within(TimeSpan.FromMilliseconds(100), tab.SelectAll);
            Assert.Equal(Count, tab.SelectedCount);
            Within(TimeSpan.FromMilliseconds(100), tab.InvertSelection);
            Assert.Equal(0, tab.SelectedCount);
            Within(TimeSpan.FromMilliseconds(300), () => tab.SelectMask("*.txt", true, false));
            Assert.Equal((Count + 2) / 3, tab.SelectedCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void Within(TimeSpan budget, Action work)
    {
        var clock = Stopwatch.StartNew();
        work();
        Assert.True(clock.Elapsed < budget, $"took {clock.ElapsedMilliseconds} ms, budget {budget.TotalMilliseconds} ms");
    }

    private static async Task Within(TimeSpan budget, Func<Task> work)
    {
        var clock = Stopwatch.StartNew();
        await work();
        Assert.True(clock.Elapsed < budget, $"took {clock.ElapsedMilliseconds} ms, budget {budget.TotalMilliseconds} ms");
    }
}
