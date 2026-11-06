using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.Core.Tests.FileSystem;

public class FolderSizeCacheTests
{
    private static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void AFolderWithTheSameLastWriteHits()
    {
        var cache = new FolderSizeCache();
        cache.Set("/a", Stamp, 42);

        Assert.Equal(42UL, cache.Get("/a", Stamp));
    }

    [Fact]
    public void AChangedLastWriteOrAnotherPathMisses()
    {
        var cache = new FolderSizeCache();
        cache.Set("/a", Stamp, 42);

        Assert.Null(cache.Get("/a", Stamp.AddSeconds(1)));
        Assert.Null(cache.Get("/b", Stamp));
    }

    [Fact]
    public void SettingAgainReplacesTheSizeWithoutGrowing()
    {
        var cache = new FolderSizeCache();
        cache.Set("/a", Stamp, 1);
        cache.Set("/a", Stamp.AddSeconds(1), 2);

        Assert.Equal(1, cache.Count);
        Assert.Equal(2UL, cache.Get("/a", Stamp.AddSeconds(1)));
    }

    [Fact]
    public void PastTheCapacityTheOldestEntriesGo()
    {
        var cache = new FolderSizeCache(3);
        for (var i = 0; i < 5; i++)
        {
            cache.Set($"/f{i}", Stamp, (ulong)i);
        }

        Assert.Equal(3, cache.Count);
        Assert.Null(cache.Get("/f0", Stamp));
        Assert.Null(cache.Get("/f1", Stamp));
        Assert.Equal(4UL, cache.Get("/f4", Stamp));
    }
}
