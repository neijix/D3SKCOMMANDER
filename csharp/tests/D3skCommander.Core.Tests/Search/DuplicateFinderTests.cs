using D3skCommander.Core.Search;
using Xunit;

namespace D3skCommander.Core.Tests.Search;

public class DuplicateFinderTests
{
    private static readonly DuplicateCriteria Content = new(false, false, true, false);

    private static DuplicateFile File(string path) => new(path, (ulong)new FileInfo(path).Length);

    private static IReadOnlyList<DuplicateFile> Files(params string[] paths) => paths.Select(File).ToList();

    private static List<string> Names(DuplicateGroup group) =>
        group.Files.Select(file => Path.GetFileName(file.Path)).ToList();

    [Fact]
    public async Task ContentGroupsEqualFilesAndSplitsSameSizeWithOtherContent()
    {
        using var dir = new SearchTempDir();
        var files = Files(
            dir.Write("a.txt", "hello"),
            dir.Write("sub/b.txt", "hello"),
            dir.Write("c.txt", "world"),
            dir.Write("d.txt", "hello world"));

        var result = await new DuplicateFinder().FindAsync(files, Content, null, TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Equal(1, group.Number);
        Assert.Equal(["a.txt", "b.txt"], Names(group));
        Assert.Equal(5UL, group.WastedBytes);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public async Task SizeAloneGroupsDifferentContent()
    {
        using var dir = new SearchTempDir();
        var files = Files(dir.Write("a", "12345"), dir.Write("b", "abcde"), dir.Write("c", "1"));

        var result = await new DuplicateFinder()
            .FindAsync(files, new DuplicateCriteria(false, true, false, false), null, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], Names(Assert.Single(result.Groups)));
    }

    [Fact]
    public async Task NameAloneGroupsSameNamesWhateverTheSizeAndSingletonsAreDropped()
    {
        using var dir = new SearchTempDir();
        var files = Files(dir.Write("x/same.txt", "1"), dir.Write("y/same.txt", "22"), dir.Write("z/other.txt", "3"));

        var result = await new DuplicateFinder()
            .FindAsync(files, new DuplicateCriteria(true, false, false, false), null, TestContext.Current.CancellationToken);

        var group = Assert.Single(result.Groups);
        Assert.Equal(["same.txt", "same.txt"], Names(group));
    }

    [Fact]
    public async Task NameAndSizeTogetherNeedBoth()
    {
        using var dir = new SearchTempDir();
        var files = Files(dir.Write("x/same.txt", "1"), dir.Write("y/same.txt", "22"), dir.Write("z/same.txt", "3"));

        var result = await new DuplicateFinder()
            .FindAsync(files, new DuplicateCriteria(true, true, false, false), null, TestContext.Current.CancellationToken);

        Assert.Equal(["same.txt", "same.txt"], Names(Assert.Single(result.Groups)));
    }

    [Fact]
    public async Task EmptyFilesFormOneGroupWithoutBeingRead()
    {
        using var dir = new SearchTempDir();
        var files = Files(dir.Write("a", ""), dir.Write("b", ""), dir.Write("c", ""), dir.Write("d", "x"));
        var reads = 0;
        var finder = new DuplicateFinder((_, _) =>
        {
            reads++;
            return Task.FromResult("h");
        });

        var result = await finder.FindAsync(files, Content, null, TestContext.Current.CancellationToken);

        Assert.Equal(3, Assert.Single(result.Groups).Files.Count);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task GroupsAreOrderedByWastedBytesAndProgressIsReported()
    {
        using var dir = new SearchTempDir();
        var files = Files(
            dir.Write("small1", "ab"),
            dir.Write("small2", "ab"),
            dir.Write("big1", new string('z', 1000)),
            dir.Write("big2", new string('z', 1000)),
            dir.Write("alone", "q"));
        var seen = new List<(long, long)>();

        var result = await new DuplicateFinder()
            .FindAsync(files, Content, (done, total) => seen.Add((done, total)), TestContext.Current.CancellationToken);

        Assert.Equal(["big1", "big2"], Names(result.Groups[0]));
        Assert.Equal(["small1", "small2"], Names(result.Groups[1]));
        Assert.Equal(2, result.Groups[1].Number);
        Assert.Equal((0, 4), seen[0]);
        Assert.Equal((4, 4), seen[^1]);
    }

    [Fact]
    public async Task AnUnreadableFileIsSkippedAndCounted()
    {
        using var dir = new SearchTempDir();
        var files = Files(dir.Write("a", "same"), dir.Write("b", "same"), dir.Write("c", "same")).ToList();
        var missing = Path.Combine(dir.Root, "c");
        System.IO.File.Delete(missing);

        var result = await new DuplicateFinder().FindAsync(files, Content, null, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], Names(Assert.Single(result.Groups)));
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task ByteCompareSplitsAForcedHashCollision()
    {
        using var dir = new SearchTempDir();
        var files = Files(dir.Write("a", "aaaa"), dir.Write("b", "aaaa"), dir.Write("c", "bbbb"));
        var collide = new DuplicateFinder((_, _) => Task.FromResult("same"));

        var trusting = await collide.FindAsync(files, Content, null, TestContext.Current.CancellationToken);
        var checking = await collide.FindAsync(
            files,
            Content with { CompareBytes = true },
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, Assert.Single(trusting.Groups).Files.Count);
        Assert.Equal(["a", "b"], Names(Assert.Single(checking.Groups)));
    }

    [Fact]
    public async Task CancellingThrowsOperationCanceled()
    {
        using var dir = new SearchTempDir();
        var files = Files(dir.Write("a", "same"), dir.Write("b", "same"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DuplicateFinder().FindAsync(files, Content, null, cts.Token));
    }

    [Fact]
    public async Task NoCriterionIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => new DuplicateFinder().FindAsync([], new DuplicateCriteria(false, false, false, false), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheEngineWalksWithItsFiltersAndReportsGroupedHits()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.txt", "dup");
        dir.Write("sub/b.txt", "dup");
        dir.Write("skip/c.txt", "dup");
        dir.Write("d.md", "dup");
        dir.Write("e.txt", "other");
        var form = new SearchForm { Roots = dir.Root, Name = "*.txt", ExcludeFolders = "skip", FindDuplicates = true };
        var query = SearchQueryBuilder.Build(form, DateTimeOffset.Now).Query!;

        var run = await SearchRun.Execute(query);

        Assert.Equal(["a.txt", "b.txt"], run.Names());
        Assert.All(run.Hits, hit => Assert.Equal(1, hit.Group));
        Assert.Equal(2, run.Done!.Hits);
        Assert.Equal(0, run.Done.Skipped);
    }

    [Fact]
    public void TheFormNeedsACriterion()
    {
        var form = new SearchForm { Roots = "/data", FindDuplicates = true, DupSize = false, DupContent = false };

        Assert.Equal(SearchFormProblem.NoDuplicateCriterion, SearchQueryBuilder.Build(form, DateTimeOffset.Now).Problem);
        form.DupName = true;
        Assert.NotNull(SearchQueryBuilder.Build(form, DateTimeOffset.Now).Query!.Duplicates);
    }
}
