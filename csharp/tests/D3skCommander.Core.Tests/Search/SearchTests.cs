using System.IO.Compression;
using D3skCommander.Core.Search;
using Xunit;

namespace D3skCommander.Core.Tests.Search;

public class SearchTests
{
    private static SearchQuery Query(SearchTempDir dir) => new() { Roots = [dir.Root] };

    [Fact]
    public async Task ByNameMaskWithExclude()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.rs", "x");
        dir.Write("b.txt", "x");
        dir.Write("sub/test_c.rs", "x");
        var run = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Name = "*.rs | test_*", Dirs = false });
        Assert.Equal(["a.rs"], run.Names());
        Assert.NotNull(run.Done);
        Assert.False(run.Done.Cancelled);
        Assert.Equal(1, run.Done.Hits);
    }

    [Fact]
    public async Task ByNameRegexIsCaseInsensitiveUnlessAsked()
    {
        using var dir = new SearchTempDir();
        dir.Write("Report1.txt", "x");
        dir.Write("report22.txt", "x");
        dir.Write("notes.txt", "x");
        var loose = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Name = "^report\\d+\\.txt$", NameIsRegex = true });
        Assert.Equal(["Report1.txt", "report22.txt"], loose.Names());
        var strict = await SearchRun.Execute(new SearchQuery
        {
            Roots = [dir.Root],
            Name = "^report\\d+\\.txt$",
            NameIsRegex = true,
            CaseSensitive = true,
        });
        Assert.Equal(["report22.txt"], strict.Names());
    }

    [Fact]
    public async Task InvalidRegexReportsAnErrorThenDone()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.txt", "x");
        var run = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Name = "(", NameIsRegex = true });
        Assert.Single(run.Errors);
        Assert.Empty(run.Hits);
        Assert.NotNull(run.Done);
        Assert.Equal(0, run.EventsAfterDone);
    }

    [Fact]
    public async Task BacktrackingOnlyRegexFeaturesAreRefused()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.txt", "abab");
        var run = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "(ab)\\1", ContentIsRegex = true });
        Assert.Single(run.Errors);
        Assert.NotNull(run.Done);
    }

    [Fact]
    public async Task ContentSearchReportsTheLine()
    {
        using var dir = new SearchTempDir();
        dir.Write("one.txt", "alpha\nbeta needle here\ngamma\n");
        dir.Write("two.txt", "nothing\n");
        var run = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "NEEDLE" });
        var hit = Assert.Single(run.Hits);
        Assert.Equal("one.txt", hit.Name);
        Assert.Equal(2, hit.Line);
        Assert.Equal("beta needle here", hit.LineText);
        var strict = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "NEEDLE", CaseSensitive = true });
        Assert.Empty(strict.Hits);
    }

    [Fact]
    public async Task ContentRegexAndWholeWords()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.txt", "cat concatenate\n");
        dir.Write("b.txt", "concatenate\n");
        var words = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "cat", WholeWords = true });
        Assert.Equal(["a.txt"], words.Names());
        var regex = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "c[a-z]+e$", ContentIsRegex = true });
        Assert.Equal(["a.txt", "b.txt"], regex.Names());
    }

    [Fact]
    public async Task ContentSkipsFoldersAndBinaryForRegexOnly()
    {
        using var dir = new SearchTempDir();
        dir.Write("bin.dat", new byte[] { 1, 0, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e' });
        Directory.CreateDirectory(Path.Combine(dir.Root, "needle_dir"));
        var plain = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "needle" });
        Assert.Equal(["bin.dat"], plain.Names());
        var regex = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "needle", ContentIsRegex = true });
        Assert.Empty(regex.Hits);
    }

    [Fact]
    public async Task NotContainingListsFilesWithoutTheText()
    {
        using var dir = new SearchTempDir();
        dir.Write("has.txt", "needle\n");
        dir.Write("lacks.txt", "hay\n");
        var run = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "needle", NotContaining = true });
        Assert.Equal(["lacks.txt"], run.Names());
    }

    [Fact]
    public async Task SizeFiltersApplyToFilesOnly()
    {
        using var dir = new SearchTempDir();
        dir.Write("small.bin", new byte[10]);
        dir.Write("mid.bin", new byte[100]);
        dir.Write("big.bin", new byte[1000]);
        Directory.CreateDirectory(Path.Combine(dir.Root, "folder"));
        var run = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], MinSize = 50, MaxSize = 500 });
        Assert.Equal(["folder", "mid.bin"], run.Names());
    }

    [Fact]
    public async Task DateFilters()
    {
        using var dir = new SearchTempDir();
        var old = dir.Write("old.txt", "x");
        var fresh = dir.Write("new.txt", "x");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(old, now.AddDays(-40));
        File.SetLastWriteTimeUtc(fresh, now.AddDays(-1));
        var newer = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], NewerThan = now.AddDays(-7) });
        Assert.Equal(["new.txt"], newer.Names());
        var older = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], OlderThan = now.AddDays(-30) });
        Assert.Equal(["old.txt"], older.Names());
        var between = await SearchRun.Execute(new SearchQuery
        {
            Roots = [dir.Root],
            NewerThan = now.AddDays(-60),
            OlderThan = now.AddDays(-30),
        });
        Assert.Equal(["old.txt"], between.Names());
    }

    [Fact]
    public async Task MaxDepthLimitsLevelsBelowTheRoot()
    {
        using var dir = new SearchTempDir();
        dir.Write("top.txt", "x");
        dir.Write("a/mid.txt", "x");
        dir.Write("a/b/deep.txt", "x");
        var one = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Dirs = false, MaxDepth = 1 });
        Assert.Equal(["top.txt"], one.Names());
        var two = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Dirs = false, MaxDepth = 2 });
        Assert.Equal(["mid.txt", "top.txt"], two.Names());
        var all = await SearchRun.Execute(Query(dir));
        Assert.Contains("deep.txt", all.Names());
    }

    [Fact]
    public async Task HiddenEntriesNeedTheFlag()
    {
        using var dir = new SearchTempDir();
        dir.Write(".secret/inner.txt", "x");
        dir.Write(".dot.txt", "x");
        dir.Write("plain.txt", "x");
        var shown = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Dirs = false });
        Assert.Equal(["plain.txt"], shown.Names());
        var all = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Dirs = false, IncludeHidden = true });
        Assert.Equal([".dot.txt", "inner.txt", "plain.txt"], all.Names());
    }

    [Fact]
    public async Task FilesOnlyAndFoldersOnly()
    {
        using var dir = new SearchTempDir();
        dir.Write("a/f.txt", "x");
        var files = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Dirs = false });
        Assert.Equal(["f.txt"], files.Names());
        var dirs = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Files = false });
        Assert.Equal(["a"], dirs.Names());
    }

    [Fact]
    public async Task SeveralRootsAndAMissingRootReportsAnError()
    {
        using var one = new SearchTempDir();
        using var two = new SearchTempDir();
        one.Write("x1.txt", "x");
        two.Write("x2.txt", "x");
        var run = await SearchRun.Execute(new SearchQuery
        {
            Roots = [one.Root, Path.Combine(one.Root, "missing"), two.Root],
            Dirs = false,
        });
        Assert.Equal(["x1.txt", "x2.txt"], run.Names());
        Assert.Single(run.Errors);
    }

    [Fact]
    public async Task CancelStopsTheSearchAndStillSaysDone()
    {
        using var dir = new SearchTempDir();
        for (var i = 0; i < 200; i++)
        {
            dir.Write("f" + i + ".txt", "x");
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new SearchRun();
        var seen = 0;
        await foreach (var step in new SearchEngine().RunAsync(Query(dir), cancel.Token))
        {
            if (step.Kind == SearchEventKind.Hit && ++seen == 3)
            {
                await cancel.CancelAsync();
            }

            if (step.Kind == SearchEventKind.Done)
            {
                Assert.True(step.Cancelled);
                Assert.True(step.Hits < 200);
                return;
            }
        }

        Assert.Fail("no Done event");
    }

    [Fact]
    public async Task ResultsStreamBeforeTheSearchEnds()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.txt", "x");
        dir.Write("b.txt", "x");
        var kinds = new List<SearchEventKind>();
        await foreach (var step in new SearchEngine().RunAsync(new SearchQuery { Roots = [dir.Root], Dirs = false }, TestContext.Current.CancellationToken))
        {
            kinds.Add(step.Kind);
        }

        Assert.Equal([SearchEventKind.Hit, SearchEventKind.Hit, SearchEventKind.Done], kinds);
    }

    [Fact]
    public async Task SearchesInsideAZipOnlyWhenAsked()
    {
        using var dir = new SearchTempDir();
        var zip = Path.Combine(dir.Root, "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var inner = archive.CreateEntry("docs/inner.txt");
            using (var writer = new StreamWriter(inner.Open()))
            {
                writer.Write("first\nzipneedle line\n");
            }

            var other = archive.CreateEntry("docs/other.md");
            using (var writer = new StreamWriter(other.Open()))
            {
                writer.Write("nothing\n");
            }
        }

        var off = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Name = "inner.txt" });
        Assert.Empty(off.Hits);
        var byName = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Name = "inner.txt", SearchArchives = true });
        var hit = Assert.Single(byName.Hits);
        Assert.Equal(Path.Combine(zip, "docs", "inner.txt"), hit.Path);
        var byText = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Content = "zipneedle", SearchArchives = true });
        var textHit = Assert.Single(byText.Hits);
        Assert.Equal("inner.txt", textHit.Name);
        Assert.Equal(2, textHit.Line);
        var rooted = await SearchRun.Execute(new SearchQuery { Roots = [zip], Name = "*.md", SearchArchives = true });
        Assert.Equal(["other.md"], rooted.Names());
    }
}
