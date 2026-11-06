using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Search;
using Xunit;

namespace D3skCommander.Core.Tests.Search;

public class SearchFilterTests
{
    [Fact]
    public async Task SeveralNameMasksAnyOneWins()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.cs", "x");
        dir.Write("b.md", "x");
        dir.Write("c.txt", "x");
        var form = new SearchForm { Roots = dir.Root, Name = " *.cs ; ;*.md ", Dirs = false };
        var run = await SearchRun.Execute(SearchQueryBuilder.Build(form, DateTimeOffset.Now).Query!);
        Assert.Equal(["a.cs", "b.md"], run.Names());
    }

    [Fact]
    public async Task ExcludedFilesAndFoldersAreNotHits()
    {
        using var dir = new SearchTempDir();
        dir.Write("a.cs", "x");
        dir.Write("a.min.js", "x");
        dir.Write("logs/b.log", "x");
        var form = new SearchForm { Roots = dir.Root, ExcludeFiles = "*.min.js;logs" };
        var run = await SearchRun.Execute(SearchQueryBuilder.Build(form, DateTimeOffset.Now).Query!);
        Assert.Equal(["a.cs", "b.log"], run.Names());
    }

    [Fact]
    public async Task AnExcludedFolderByNameIsNeverListed()
    {
        using var dir = new SearchTempDir();
        dir.Write("keep/a.txt", "x");
        dir.Write("node_modules/deep/b.txt", "x");
        dir.Write("sub/.git/c.txt", "x");
        var listed = new List<string>();
        var engine = new SearchEngine(new ListingProvider(listed), new ArchiveFileSystemProvider());
        var form = new SearchForm { Roots = dir.Root, ExcludeFolders = "node_modules; .git" };
        var query = SearchQueryBuilder.Build(form, DateTimeOffset.Now).Query!;
        var names = new List<string>();
        await foreach (var step in engine.RunAsync(query, TestContext.Current.CancellationToken))
        {
            if (step.Kind == SearchEventKind.Hit)
            {
                names.Add(step.Hit!.Name);
            }
        }

        Assert.Contains("a.txt", names);
        Assert.DoesNotContain("b.txt", names);
        Assert.DoesNotContain("c.txt", names);
        Assert.DoesNotContain("node_modules", names);
        Assert.DoesNotContain(listed, p => p.Contains("node_modules", StringComparison.Ordinal) || p.Contains(".git", StringComparison.Ordinal));
        Assert.Contains(listed, p => p.EndsWith("keep", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAbsoluteExcludedPathPrunesItAndWhatIsBelow()
    {
        using var dir = new SearchTempDir();
        dir.Write("one/a.txt", "x");
        dir.Write("two/b.txt", "x");
        dir.Write("two/inner/c.txt", "x");
        dir.Write("twofold/d.txt", "x");
        var excluded = Path.Combine(dir.Root, "two") + Path.DirectorySeparatorChar;
        var form = new SearchForm { Roots = dir.Root, ExcludeFolders = excluded, Dirs = false };
        var run = await SearchRun.Execute(SearchQueryBuilder.Build(form, DateTimeOffset.Now).Query!);
        Assert.Equal(["a.txt", "d.txt"], run.Names());
    }

    [Fact]
    public async Task ALinkLoopEndsWhenLinksAreFollowed()
    {
        using var dir = new SearchTempDir();
        dir.Write("sub/a.txt", "x");
        MakeLink(Path.Combine(dir.Root, "sub", "up"), dir.Root);
        var form = new SearchForm { Roots = dir.Root, FollowLinks = true, Name = "a.txt" };
        var run = await SearchRun.Execute(SearchQueryBuilder.Build(form, DateTimeOffset.Now).Query!);
        Assert.Equal(["a.txt"], run.Names());
        Assert.NotNull(run.Done);
    }

    [Fact]
    public async Task ALinkedFolderIsEnteredOnlyWhenAsked()
    {
        using var dir = new SearchTempDir();
        using var outside = new SearchTempDir();
        outside.Write("far.txt", "x");
        MakeLink(Path.Combine(dir.Root, "link"), outside.Root);
        var off = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Name = "far.txt" });
        Assert.Empty(off.Names());
        var on = await SearchRun.Execute(new SearchQuery { Roots = [dir.Root], Name = "far.txt", FollowLinks = true });
        Assert.Equal(["far.txt"], on.Names());
    }

    private static void MakeLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("this OS refused to create a directory link");
        }
    }

    /// <summary>The local provider, noting every folder it is asked to list.</summary>
    private sealed class ListingProvider(List<string> listed) : IFileSystemProvider
    {
        private readonly LocalFileSystemProvider _inner = new();

        public bool IsReadOnly => _inner.IsReadOnly;

        public IAsyncEnumerable<FileResult<Entry>> ListAsync(string directory, bool showHidden, CancellationToken cancellationToken)
        {
            listed.Add(directory);
            return _inner.ListAsync(directory, showHidden, cancellationToken);
        }

        public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) =>
            _inner.GetEntryAsync(path, cancellationToken);

        public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) =>
            _inner.OpenReadAsync(path, cancellationToken);

        public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) =>
            _inner.OpenWriteAsync(path, cancellationToken);

        public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
            _inner.CreateFolderAsync(path, cancellationToken);

        public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) =>
            _inner.RenameAsync(path, newName, cancellationToken);

        public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(path, cancellationToken);
    }
}
