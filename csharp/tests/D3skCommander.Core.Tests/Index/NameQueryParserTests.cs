using D3skCommander.Core.Index;
using Xunit;

namespace D3skCommander.Core.Tests.Index;

public class NameQueryParserTests
{
    [Fact]
    public void PlainWordsAreSubstringTermsAndWildcardsAreMasks()
    {
        var query = NameQueryParser.Parse("  Report *.pdf  a?c ");

        Assert.Equal(["Report", "*.pdf", "a?c"], query.Terms.Select(term => term.Text).ToArray());
        Assert.Equal([false, true, true], query.Terms.Select(term => term.Mask is not null).ToArray());
        Assert.Equal("Report", query.RankText);
        Assert.False(query.IsEmpty);
    }

    [Fact]
    public void QuotesKeepASpaceInsideAWord()
    {
        var query = NameQueryParser.Parse("\"my report\" folder:\"old docs\"");

        Assert.Equal(["my report"], query.Terms.Select(term => term.Text).ToArray());
        Assert.Equal(["old docs"], query.FolderParts);
    }

    [Fact]
    public void ExtTakesSeveralExtensionsWithOrWithoutDots()
    {
        var query = NameQueryParser.Parse("ext:PDF;.docx,*.txt ext:md");

        Assert.Equal(["pdf", "docx", "txt", "md"], query.Extensions);
        Assert.Empty(query.Terms);
    }

    [Theory]
    [InlineData("size:>100M", 100L * 1024 * 1024 + 1, long.MaxValue)]
    [InlineData("size:>=100M", 100L * 1024 * 1024, long.MaxValue)]
    [InlineData("size:<1K", 0, 1023)]
    [InlineData("size:<=1K", 0, 1024)]
    [InlineData("size:10M..1G", 10L * 1024 * 1024, 1024L * 1024 * 1024)]
    [InlineData("size:..2k", 0, 2048)]
    [InlineData("size:1.5k..", 1536, long.MaxValue)]
    [InlineData("size:500", 500, 500)]
    [InlineData("size:2KB", 2048, 2048)]
    public void SizeReadsComparisonsRangesAndUnits(string text, long min, long max)
    {
        var query = NameQueryParser.Parse(text);

        Assert.Equal(new SizeRange(min, max), query.Size);
        Assert.Empty(query.Ignored);
    }

    [Fact]
    public void TwoSizeFiltersIntersect()
    {
        var query = NameQueryParser.Parse("size:>1K size:<1M");

        Assert.Equal(new SizeRange(1025, 1024 * 1024 - 1), query.Size);
    }

    [Fact]
    public void FolderAndTypeFilters()
    {
        var query = NameQueryParser.Parse("folder:docs folder:2024 type:folder");

        Assert.Equal(["docs", "2024"], query.FolderParts);
        Assert.Equal(NameKind.Folder, query.Kind);
        Assert.Equal(NameKind.File, NameQueryParser.Parse("type:file").Kind);
        Assert.Equal(NameKind.File, NameQueryParser.Parse("type:FILES").Kind);
    }

    [Theory]
    [InlineData("size:>")]
    [InlineData("size:")]
    [InlineData("ext:")]
    [InlineData("folder:")]
    [InlineData("type:")]
    [InlineData("")]
    [InlineData("   ")]
    public void AHalfTypedFilterMatchesNothingMoreThanNoFilter(string text) =>
        Assert.True(NameQueryParser.Parse(text).IsEmpty);

    [Theory]
    [InlineData("size:12x")]
    [InlineData("size:>x")]
    [InlineData("size:5M..1M")]
    [InlineData("type:banana")]
    public void AFilterThatCannotBeReadIsIgnoredAndReported(string text)
    {
        var query = NameQueryParser.Parse(text + " abc");

        Assert.Equal([text], query.Ignored);
        Assert.Null(query.Size);
        Assert.Equal(NameKind.Any, query.Kind);
        Assert.Equal(["abc"], query.Terms.Select(term => term.Text).ToArray());
    }

    [Fact]
    public void AnUnknownKeyIsAPlainWord()
    {
        var query = NameQueryParser.Parse("c:temp");

        Assert.Equal(["c:temp"], query.Terms.Select(term => term.Text).ToArray());
    }
}
