using D3skCommander.Core.Search;
using Xunit;

namespace D3skCommander.Core.Tests.Search;

public class SearchQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static SearchForm Form() => new() { Roots = "/data" };

    [Fact]
    public void DefaultsMakeAPlainQuery()
    {
        var result = SearchQueryBuilder.Build(Form(), Now);
        var query = result.Query!;
        Assert.Equal(["/data"], query.Roots);
        Assert.Equal("*", query.Name);
        Assert.Null(query.Content);
        Assert.Null(query.MaxDepth);
        Assert.Null(query.MinSize);
        Assert.Null(query.MaxSize);
        Assert.Null(query.NewerThan);
        Assert.Null(query.OlderThan);
        Assert.True(query.Files && query.Dirs);
        Assert.False(query.SearchArchives);
    }

    [Fact]
    public void RootsAreSplitAndTrimmed()
    {
        var form = Form();
        form.Roots = " /a ; /b;;  ;/c d ";
        Assert.Equal(["/a", "/b", "/c d"], SearchQueryBuilder.Build(form, Now).Query!.Roots);
        form.Roots = " ; ";
        Assert.Equal(SearchFormProblem.NoFolder, SearchQueryBuilder.Build(form, Now).Problem);
    }

    [Fact]
    public void SizesParseAndEmptyMeansNone()
    {
        var form = Form();
        form.MinSize = "10K";
        form.MaxSize = " 2M ";
        var query = SearchQueryBuilder.Build(form, Now).Query!;
        Assert.Equal(10UL * 1024, query.MinSize);
        Assert.Equal(2UL * 1024 * 1024, query.MaxSize);
        form.MinSize = "abc";
        var bad = SearchQueryBuilder.Build(form, Now);
        Assert.Equal(SearchFormProblem.BadSize, bad.Problem);
        Assert.Equal("abc", bad.Detail);
        form.MinSize = "3M";
        Assert.Equal(SearchFormProblem.SizeRange, SearchQueryBuilder.Build(form, Now).Problem);
    }

    [Fact]
    public void DaysBecomeInstants()
    {
        var form = Form();
        form.NewerDays = "7";
        form.OlderDays = "30";
        var query = SearchQueryBuilder.Build(form, Now).Query!;
        Assert.Equal(Now.AddDays(-7), query.NewerThan);
        Assert.Equal(Now.AddDays(-30), query.OlderThan);
        form.NewerDays = "-1";
        Assert.Equal(SearchFormProblem.BadDays, SearchQueryBuilder.Build(form, Now).Problem);
        form.NewerDays = "99999999";
        Assert.Equal(SearchFormProblem.BadDays, SearchQueryBuilder.Build(form, Now).Problem);
    }

    [Fact]
    public void DepthNameAndText()
    {
        var form = Form();
        form.DepthLimited = true;
        form.Depth = 3;
        form.Name = "  ";
        form.Text = "needle";
        form.TextIsRegex = true;
        form.NotContaining = true;
        form.InArchives = true;
        var query = SearchQueryBuilder.Build(form, Now).Query!;
        Assert.Equal(3, query.MaxDepth);
        Assert.Equal("*", query.Name);
        Assert.Equal("needle", query.Content);
        Assert.True(query.ContentIsRegex && query.NotContaining && query.SearchArchives);
    }

    [Fact]
    public void NeedsFilesOrFolders()
    {
        var form = Form();
        form.Files = false;
        form.Dirs = false;
        Assert.Equal(SearchFormProblem.NoKind, SearchQueryBuilder.Build(form, Now).Problem);
        form.Dirs = true;
        Assert.True(SearchQueryBuilder.Build(form, Now).IsSuccess);
    }
}
