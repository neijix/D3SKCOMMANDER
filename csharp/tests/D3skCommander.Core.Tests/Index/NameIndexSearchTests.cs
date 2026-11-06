using D3skCommander.Core.Index;
using Xunit;
using static D3skCommander.Core.Tests.Index.IndexTree;

namespace D3skCommander.Core.Tests.Index;

public class NameIndexSearchTests
{
    private readonly NameIndex _index = Small();

    [Fact]
    public void APlainTermIsACaseInsensitiveSubstringOfTheName()
    {
        var result = Run(_index, "REPORT");

        Assert.Equal(["reports", "Report.pdf", "report.txt", "my report final.docx"], Names(result));
        Assert.Equal(4, result.Total);
    }

    [Fact]
    public void TermsAreAnded()
    {
        Assert.Equal(["my report final.docx"], Names(Run(_index, "report final")));
        Assert.Equal(0, Run(_index, "report nothing").Total);
    }

    [Fact]
    public void AWildcardTermMatchesTheWholeName()
    {
        Assert.Equal(["Report.pdf", "q1.pdf"], Names(Run(_index, "*.pdf")).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["q1.pdf"], Names(Run(_index, "q?.pdf")));
        Assert.Equal(["Report.pdf"], Names(Run(_index, "re*.pdf")));
        Assert.Equal(0, Run(_index, "port*").Total);
    }

    [Fact]
    public void ExtFiltersFilesByExtension()
    {
        Assert.Equal(["Report.pdf", "q1.pdf"], Names(Run(_index, "ext:pdf")).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(3, Run(_index, "ext:pdf;docx").Total);
        Assert.Equal(["Report.pdf"], Names(Run(_index, "report ext:PDF")));
        Assert.Equal(0, Run(_index, "ext:reports").Total);
    }

    [Fact]
    public void SizeFiltersFilesOnly()
    {
        Assert.Equal(["big.iso"], Names(Run(_index, "size:>1G")));
        Assert.Equal(["report.txt", "readme"], Names(Run(_index, "size:<1K")));
        Assert.Equal(["q1.pdf"], Names(Run(_index, "size:1M..1G")));
        Assert.Equal(["Report.pdf"], Names(Run(_index, "size:2000")));
    }

    [Fact]
    public void CheapWildcardShapesMatchLikeTheMask()
    {
        Assert.Equal(["Report.pdf", "q1.pdf"], Names(Run(_index, "*.PDF")));
        Assert.Equal(["reports", "Report.pdf", "report.txt"], Names(Run(_index, "rep*")));
        Assert.Equal(["reports", "Report.pdf", "report.txt", "my report final.docx"], Names(Run(_index, "*port*")));
        Assert.Equal(["reports", "Report.pdf", "report.txt"], Names(Run(_index, "r?port*")));
        Assert.Equal(["report.txt"], Names(Run(_index, "report.t?t")));
    }

    [Fact]
    public void FolderMatchesTheParentPath()
    {
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal(["q1.pdf", "big.iso"], Names(Run(_index, $"folder:data{sep}reports")));
        Assert.Equal(["readme"], Names(Run(_index, "folder:ANN")));
        Assert.Equal(["ann"], Names(Run(_index, "folder:home type:folder")));
        Assert.Equal(["notes", "reports"], Names(Run(_index, "folder:data type:folder")));
    }

    [Fact]
    public void TypeSelectsFilesOrFolders()
    {
        Assert.Equal(["reports"], Names(Run(_index, "type:folder rep")));
        Assert.DoesNotContain("readme", Names(Run(_index, "type:folder")));
        Assert.DoesNotContain("notes", Names(Run(_index, "type:file")));
        Assert.Equal(6, Run(_index, "type:file").Total);
        Assert.Equal(5, Run(_index, "type:folder").Total);
    }

    [Fact]
    public void AnEmptyQueryMatchesNothing()
    {
        Assert.Equal(0, Run(_index, "").Total);
        Assert.Empty(Run(_index, "size:>").Hits);
    }

    [Fact]
    public void ExactNameThenPrefixThenTheRestAndShorterPathsFirst()
    {
        var b = new NameIndexBuilder();
        var stamp = DateTimeOffset.UnixEpoch;
        var root = b.Add("/r", -1, 0, stamp, true);
        var deep = b.Add("deep", root, 0, stamp, true);
        var deeper = b.Add("deeper", deep, 0, stamp, true);
        b.Add("my-cat-pics", root, 1, stamp, false);
        b.Add("cat", deeper, 1, stamp, false);
        b.Add("catalog", deeper, 1, stamp, false);
        b.Add("CAT", root, 1, stamp, false);
        b.Add("cat.txt", root, 1, stamp, false);
        b.Add("scatter", root, 1, stamp, false);
        var index = b.Build(stamp);

        var result = Run(index, "cat");

        Assert.Equal(["CAT", "cat", "cat.txt", "catalog", "scatter", "my-cat-pics"], Names(result));
        Assert.Equal(["/r/CAT", "/r/deep/deeper/cat"], result.Hits.Take(2).Select(static hit => hit.Path.Replace('\\', '/')).ToArray());
    }

    [Fact]
    public void TheCapKeepsTheBestAndTheTotalCountsEverything()
    {
        var b = new NameIndexBuilder();
        var stamp = DateTimeOffset.UnixEpoch;
        var root = b.Add("/r", -1, 0, stamp, true);
        for (var i = 0; i < 200_000; i++)
        {
            b.Add("file" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), root, i, stamp, false);
        }

        b.Add("file", root, 1, stamp, false);
        var index = b.Build(stamp);

        var result = index.Search(NameQueryParser.Parse("file"), 50, TestContext.Current.CancellationToken);

        Assert.Equal(200_001, result.Total);
        Assert.Equal(50, result.Hits.Count);
        Assert.Equal("file", result.Hits[0].Name);
        Assert.All(result.Hits.Skip(1), hit => Assert.StartsWith("file", hit.Name, StringComparison.Ordinal));
        Assert.Equal(result.Hits.Skip(1).Select(static hit => hit.Name.Length).Order().ToArray(), result.Hits.Skip(1).Select(static hit => hit.Name.Length).ToArray());
    }

    [Fact]
    public void AHitCarriesItsPathFolderSizeAndTime()
    {
        var hit = Run(_index, "q1").Hits.Single();

        Assert.Equal(Path.Combine("/data", "reports", "q1.pdf"), hit.Path);
        Assert.Equal(Path.Combine("/data", "reports"), hit.Folder);
        Assert.Equal(5_000_000, hit.Size);
        Assert.False(hit.IsFolder);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), hit.Modified);
        Assert.Equal(Path.GetDirectoryName("/data"), Run(_index, "type:folder data").Hits.Single(static h => h.Name == "/data").Folder);
    }

    [Fact]
    public void ACancelledSearchThrows()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => _index.Search(NameQueryParser.Parse("report"), 10, cancel.Token));
    }

    [Fact]
    public void ABadParentIsRefusedByTheBuilder()
    {
        var b = new NameIndexBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => b.Add("x", 3, 0, DateTimeOffset.UnixEpoch, false));
    }
}
