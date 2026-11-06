using D3skCommander.Core.Index;
using Xunit;
using static D3skCommander.Core.Tests.Index.IndexTree;

namespace D3skCommander.Core.Tests.Index;

public sealed class IndexServiceTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "d3sk-idx-svc-" + Guid.NewGuid().ToString("N"));
    private readonly string _tree;

    public IndexServiceTests()
    {
        _tree = Path.Combine(_folder, "tree");
        Directory.CreateDirectory(_tree);
        File.WriteAllText(Path.Combine(_tree, "hello.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private IndexService Service(TimeProvider? time = null) =>
        new(NameIndexFile.PathIn(_folder), () => new IndexOptions([_tree], []), time);

    [Fact]
    public async Task BuildingSavesTheFileAndPublishesTheIndex()
    {
        using var service = Service();
        var changes = 0;
        service.Changed += () => Interlocked.Increment(ref changes);
        Assert.False(service.HasFile);
        Assert.Null(service.Current);

        await service.BuildAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.False(service.IsBuilding);
        Assert.Equal(2, service.Current!.Count);
        Assert.True(service.HasFile);
        Assert.Equal(1, Run(service.Current, "hello").Total);
        Assert.True(changes >= 2);
        Assert.Equal(service.Current.BuiltAt.ToUnixTimeSeconds(), service.SavedAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task ASecondServiceLoadsWhatTheFirstSaved()
    {
        using (var first = Service())
        {
            await first.BuildAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        }

        using var second = Service();
        Assert.Null(second.Current);
        Assert.True(await second.LoadAsync());

        Assert.Equal(2, second.Current!.Count);
        Assert.True(await second.LoadAsync());
    }

    [Fact]
    public async Task LoadingWithNoFileOrABadFileSaysNo()
    {
        using var service = Service();
        Assert.False(await service.LoadAsync());

        Directory.CreateDirectory(Path.GetDirectoryName(service.File)!);
        await File.WriteAllBytesAsync(service.File, [1, 2, 3, 4, 5], TestContext.Current.CancellationToken);
        Assert.False(await service.LoadAsync());
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task ABuildRunningIsSharedAndCancellable()
    {
        for (var i = 0; i < 3_000; i++)
        {
            File.WriteAllText(Path.Combine(_tree, $"f{i}.txt"), "");
        }

        using var service = Service();
        var first = service.BuildAsync();
        var second = service.BuildAsync();
        Assert.Same(first, second);
        service.Cancel();
        await first.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.False(service.IsBuilding);
        Assert.True(service.WasCancelled);
        Assert.Null(service.Current);
        Assert.False(service.HasFile);

        await service.BuildAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.False(service.WasCancelled);
        Assert.Equal(3_002, service.Current!.Count);
    }

    [Fact]
    public async Task TheOldIndexServesUntilTheNewOneReplacesIt()
    {
        using var service = Service();
        await service.BuildAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        var old = service.Current;
        File.WriteAllText(Path.Combine(_tree, "later.txt"), "");

        var rebuild = service.BuildAsync();
        Assert.NotNull(service.Current);
        await rebuild.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.NotSame(old, service.Current);
        Assert.Equal(3, service.Current!.Count);
    }

    [Theory]
    [InlineData(24, 25, true)]
    [InlineData(24, 24, true)]
    [InlineData(24, 23, false)]
    [InlineData(0, 1000, false)]
    [InlineData(-1, 1000, false)]
    [InlineData(1, 2, true)]
    public void AnIndexIsDueOnlyOnceOlderThanTheSetting(int hours, int ageHours, bool due)
    {
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(due, IndexRefresh.IsDue(now.AddHours(-ageHours), now, hours));
    }

    [Fact]
    public void NoIndexIsNeverDue() => Assert.False(IndexRefresh.IsDue(null, DateTimeOffset.UtcNow, 24));
}
