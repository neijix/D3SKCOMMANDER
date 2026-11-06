using D3skCommander.Core.Search;
using Xunit;

namespace D3skCommander.Core.Tests.Search;

public class SearchMemoryTests
{
    [Fact]
    public void HistoryKeepsTheLatestFirstWithoutDuplicatesOrBlanks()
    {
        var history = new SearchHistory();
        history.Add(SearchHistory.Name, "*.cs");
        history.Add(SearchHistory.Name, "*.md");
        history.Add(SearchHistory.Name, "  ");
        history.Add(SearchHistory.Name, "*.cs");
        history.Add(SearchHistory.Name, "*.CS");
        Assert.Equal(["*.CS", "*.cs", "*.md"], history.Get(SearchHistory.Name));
        Assert.Empty(history.Get(SearchHistory.Text));
    }

    [Fact]
    public void HistoryIsCappedAtTwenty()
    {
        var history = new SearchHistory();
        for (var i = 0; i < 30; i++)
        {
            history.Add(SearchHistory.Roots, "/r" + i);
        }

        var roots = history.Get(SearchHistory.Roots);
        Assert.Equal(SearchHistory.Capacity, roots.Count);
        Assert.Equal("/r29", roots[0]);
        Assert.Equal("/r10", roots[^1]);
    }

    [Fact]
    public void HistoryAndTemplatesSurviveARoundTrip()
    {
        using var dir = new SearchTempDir();
        using (var store = new SearchHistoryStore(dir.Root))
        {
            store.Current.Add(SearchHistory.Text, "needle");
            store.Save(store.Current);
        }

        using (var store = new SearchTemplateStore(dir.Root))
        {
            store.Current.Save("code", new SearchForm { Name = "*.cs", FollowLinks = true, ExcludeFolders = "bin" });
            store.Save(store.Current);
        }

        using var history = new SearchHistoryStore(dir.Root);
        Assert.Equal(["needle"], history.Current.Get(SearchHistory.Text));
        using var templates = new SearchTemplateStore(dir.Root);
        var loaded = templates.Current.Load("code");
        Assert.NotNull(loaded);
        Assert.Equal("*.cs", loaded.Name);
        Assert.True(loaded.FollowLinks);
        Assert.Equal("bin", loaded.ExcludeFolders);
    }

    [Fact]
    public void MalformedFilesGiveEmptyData()
    {
        using var dir = new SearchTempDir();
        dir.Write("search-history.json", "{ not json at all ]");
        dir.Write("search-templates.json", "[1,2,3]");
        using var history = new SearchHistoryStore(dir.Root);
        using var templates = new SearchTemplateStore(dir.Root);
        Assert.Empty(history.Current.Get(SearchHistory.Name));
        Assert.Empty(templates.Current.Names());
    }

    [Fact]
    public void TemplatesSaveCopiesLoadAndDelete()
    {
        var templates = new SearchTemplates();
        var form = new SearchForm { Name = "a" };
        templates.Save("Zed", form);
        templates.Save("alpha", new SearchForm { Name = "b" });
        form.Name = "changed";
        Assert.Equal(["alpha", "Zed"], templates.Names());
        Assert.Equal("a", templates.Load("Zed")!.Name);
        Assert.True(templates.Delete("Zed"));
        Assert.False(templates.Delete("Zed"));
        Assert.Null(templates.Load("Zed"));
    }
}
