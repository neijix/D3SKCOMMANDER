using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Providers;

/// <summary>The one provider contract. This row runs it on Local, writable and read-only.</summary>
public partial class ProviderContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return [new LocalCase(readOnly: false)];
        yield return [new LocalCase(readOnly: true)];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReportsTheReadOnlyFlag(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        Assert.Equal(providerCase.ReadOnly, fixture.Provider.IsReadOnly);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ListingStreamsPreparedFiles(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var items = await Collect(fixture.Provider.ListAsync(fixture.Directory, showHidden: true, CancellationToken.None));
        var names = Names(items);
        foreach (var name in fixture.FileNames)
        {
            Assert.Contains(name, names);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ListingOfAMissingDirectoryIsNotFound(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var missing = fixture.Combine(fixture.Directory, "no-such-folder");
        var items = await Collect(fixture.Provider.ListAsync(missing, showHidden: true, CancellationToken.None));
        Assert.Single(items);
        AssertKind(items[0].Error, ErrorKind.NotFound, missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CancelledListingIsATypedErrorAndYieldsNothing(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var missing = fixture.Combine(fixture.Directory, "no-such-folder");
        var items = await Collect(fixture.Provider.ListAsync(missing, showHidden: true, cts.Token));
        Assert.Single(items);
        AssertKind(items[0].Error, ErrorKind.Cancelled, missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ListingStopsWhenCancelled(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var full = await Collect(fixture.Provider.ListAsync(fixture.Directory, showHidden: true, CancellationToken.None));
        var fullCount = full.Count(item => item.IsSuccess);
        Assert.True(fullCount >= 3);

        using var cts = new CancellationTokenSource();
        var items = new List<FileResult<Entry>>();
        await foreach (var item in fixture.Provider.ListAsync(fixture.Directory, showHidden: true, cts.Token))
        {
            items.Add(item);
            if (items.Count == 1)
            {
                cts.Cancel();
            }
        }

        var successes = items.Count(item => item.IsSuccess);
        Assert.True(successes >= 1);
        Assert.True(successes < fullCount);
        AssertKind(items[^1].Error, ErrorKind.Cancelled, fixture.Directory);
        foreach (var item in items.Take(items.Count - 1))
        {
            Assert.True(item.IsSuccess, item.Error?.Message);
        }
    }

    [Fact]
    public async Task LocalListingMatchesTheLocalFileSystem()
    {
        using var fixture = new LocalProviderFixture();
        await AssertSameListing(fixture.Directory, showHidden: false);
        await AssertSameListing(fixture.Directory, showHidden: true);
    }

    [Fact]
    public async Task DeletingADirectoryLinkLeavesTheTarget()
    {
        using var fixture = new LocalProviderFixture();
        var target = fixture.Combine(fixture.Directory, "target-dir");
        Directory.CreateDirectory(target);
        var kept = fixture.Combine(target, "kept.txt");
        File.WriteAllText(kept, "kept");
        var link = fixture.Combine(fixture.Directory, "link-dir");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("this OS refused to create a directory link");
        }

        var deleted = await fixture.Provider.DeleteAsync(link, CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(link) || Directory.Exists(link));
    }

    private static async Task AssertSameListing(string directory, bool showHidden)
    {
        var expected = LocalFileSystem.ListDirectory(directory, showHidden);
        Assert.True(expected.IsSuccess, expected.Error?.Message);
        var actual = await Collect(new LocalFileSystemProvider().ListAsync(directory, showHidden, CancellationToken.None));
        Assert.Equal(expected.Value.Count, actual.Count);
        for (var i = 0; i < actual.Count; i++)
        {
            Assert.True(actual[i].IsSuccess, actual[i].Error?.Message);
            AssertSame(expected.Value[i], actual[i].Value);
        }
    }

    private static void AssertSame(Entry expected, Entry actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.IsDirectory, actual.IsDirectory);
        Assert.Equal(expected.IsLink, actual.IsLink);
        Assert.Equal(expected.IsParent, actual.IsParent);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.Modified, actual.Modified);
        Assert.Equal(expected.Hidden, actual.Hidden);
        Assert.Equal(expected.ReadOnly, actual.ReadOnly);
        Assert.Equal(expected.Attributes, actual.Attributes);
    }

    private static async Task<List<FileResult<Entry>>> Collect(IAsyncEnumerable<FileResult<Entry>> items)
    {
        var list = new List<FileResult<Entry>>();
        await foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    private static List<string> Names(IEnumerable<FileResult<Entry>> items)
    {
        var names = new List<string>();
        foreach (var item in items)
        {
            Assert.True(item.IsSuccess, item.Error?.Message);
            names.Add(item.Value.Name);
        }

        return names;
    }

    private static void AssertKind(OperationError? error, ErrorKind kind, string path)
    {
        Assert.NotNull(error);
        Assert.Equal(kind, error.Kind);
        Assert.Equal(path, error.Path);
    }

    private static void AssertReadOnly(OperationError? error, string path)
    {
        Assert.NotNull(error);
        Assert.Equal(ErrorKind.Unsupported, error.Kind);
        Assert.Equal(FileSystemProvider.ReadOnlyMessage, error.Message);
        Assert.Equal(path, error.Path);
    }

    private static async Task<byte[]> ReadAll(IFileSystemProvider provider, string path)
    {
        var opened = await provider.OpenReadAsync(path, CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        await using var stream = opened.Value;
        Assert.True(stream.CanRead);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
}
