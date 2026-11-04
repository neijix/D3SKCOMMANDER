using D3skCommander.Core.Common;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Providers;

public partial class ProviderContractTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task WriteStreamCreatesAndOverwritesWhenWritable(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var created = fixture.Combine(fixture.Directory, "written.bin");
        var payload = new byte[] { 9, 8, 7 };
        if (fixture.Provider.IsReadOnly)
        {
            var refusedNew = await fixture.Provider.OpenWriteAsync(created, CancellationToken.None);
            var refusedOld = await fixture.Provider.OpenWriteAsync(fixture.SampleFilePath, CancellationToken.None);
            AssertReadOnly(refusedNew.Error, created);
            AssertReadOnly(refusedOld.Error, fixture.SampleFilePath);
            AssertKind(
                (await fixture.Provider.GetEntryAsync(created, CancellationToken.None)).Error,
                ErrorKind.NotFound,
                created);
            Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
            return;
        }

        await Write(fixture.Provider, created, payload);
        Assert.Equal(payload, await ReadAll(fixture.Provider, created));
        var shorter = new byte[] { 4, 4 };
        await Write(fixture.Provider, fixture.SampleFilePath, shorter);
        Assert.Equal(shorter, await ReadAll(fixture.Provider, fixture.SampleFilePath));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CreateFolderAddsADirectoryWhenWritable(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var folder = fixture.Combine(fixture.Directory, "brand-new");
        var created = await fixture.Provider.CreateFolderAsync(folder, CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(created.Error, folder);
            var listed = await Collect(fixture.Provider.ListAsync(fixture.Directory, true, CancellationToken.None));
            Assert.DoesNotContain(Names(listed), name => name == "brand-new");
            return;
        }

        Assert.True(created.IsSuccess, created.Error?.Message);
        var info = await fixture.Provider.GetEntryAsync(folder, CancellationToken.None);
        Assert.True(info.IsSuccess, info.Error?.Message);
        Assert.True(info.Value.IsDirectory);
        var again = await fixture.Provider.CreateFolderAsync(folder, CancellationToken.None);
        AssertKind(again.Error, ErrorKind.AlreadyExists, folder);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CreateFolderRequiresAnExistingParent(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var folder = fixture.Combine(fixture.Combine(fixture.Directory, "missing-parent"), "child");
        var created = await fixture.Provider.CreateFolderAsync(folder, CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(created.Error, folder);
            return;
        }

        AssertKind(created.Error, ErrorKind.NotFound, folder);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CreateFolderOntoAFileIsATypedError(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var created = await fixture.Provider.CreateFolderAsync(fixture.SampleFilePath, CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(created.Error, fixture.SampleFilePath);
        }
        else
        {
            AssertKind(created.Error, ErrorKind.AlreadyExists, fixture.SampleFilePath);
        }

        Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RenameMovesTheFileWhenWritable(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var renamed = await fixture.Provider.RenameAsync(fixture.SampleFilePath, "renamed.txt", CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(renamed.Error, fixture.SampleFilePath);
            Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
            return;
        }

        Assert.True(renamed.IsSuccess, renamed.Error?.Message);
        Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, renamed.Value));
        var info = await fixture.Provider.GetEntryAsync(renamed.Value, CancellationToken.None);
        Assert.True(info.IsSuccess, info.Error?.Message);
        Assert.Equal("renamed.txt", info.Value.Name);
        AssertKind(
            (await fixture.Provider.GetEntryAsync(fixture.SampleFilePath, CancellationToken.None)).Error,
            ErrorKind.NotFound,
            fixture.SampleFilePath);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RenameOntoAnExistingNameIsATypedError(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var renamed = await fixture.Provider.RenameAsync(fixture.SampleFilePath, "b.txt", CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(renamed.Error, fixture.SampleFilePath);
        }
        else
        {
            AssertKind(renamed.Error, ErrorKind.AlreadyExists, fixture.Combine(fixture.Directory, "b.txt"));
        }

        Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RenameRejectsASeparatorInTheName(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var renamed = await fixture.Provider.RenameAsync(fixture.SampleFilePath, "a/b", CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(renamed.Error, fixture.SampleFilePath);
        }
        else
        {
            AssertKind(renamed.Error, ErrorKind.InvalidData, fixture.SampleFilePath);
        }

        Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DeleteRemovesAFileWhenWritable(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var deleted = await fixture.Provider.DeleteAsync(fixture.SampleFilePath, CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(deleted.Error, fixture.SampleFilePath);
            Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
            return;
        }

        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        AssertKind(
            (await fixture.Provider.GetEntryAsync(fixture.SampleFilePath, CancellationToken.None)).Error,
            ErrorKind.NotFound,
            fixture.SampleFilePath);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DeleteRemovesAFolderTreeWhenWritable(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        if (fixture.Provider.IsReadOnly)
        {
            var refused = await fixture.Provider.DeleteAsync(fixture.Directory, CancellationToken.None);
            AssertReadOnly(refused.Error, fixture.Directory);
            Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
            return;
        }

        var folder = fixture.Combine(fixture.Directory, "tree");
        var created = await fixture.Provider.CreateFolderAsync(folder, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var child = fixture.Combine(folder, "inside.txt");
        await Write(fixture.Provider, child, fixture.SampleBytes);
        var deleted = await fixture.Provider.DeleteAsync(folder, CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        AssertKind(
            (await fixture.Provider.GetEntryAsync(folder, CancellationToken.None)).Error,
            ErrorKind.NotFound,
            folder);
        AssertKind(
            (await fixture.Provider.GetEntryAsync(child, CancellationToken.None)).Error,
            ErrorKind.NotFound,
            child);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DeleteOfAMissingPathIsATypedError(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var missing = fixture.Combine(fixture.Directory, "missing.txt");
        var deleted = await fixture.Provider.DeleteAsync(missing, CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(deleted.Error, missing);
            return;
        }

        AssertKind(deleted.Error, ErrorKind.NotFound, missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task WriteOfADirectoryIsATypedError(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var opened = await fixture.Provider.OpenWriteAsync(fixture.Directory, CancellationToken.None);
        if (fixture.Provider.IsReadOnly)
        {
            AssertReadOnly(opened.Error, fixture.Directory);
        }
        else
        {
            AssertKind(opened.Error, ErrorKind.InvalidData, fixture.Directory);
        }

        var info = await fixture.Provider.GetEntryAsync(fixture.Directory, CancellationToken.None);
        Assert.True(info.IsSuccess, info.Error?.Message);
        Assert.True(info.Value.IsDirectory);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CancelledMutationChangesNothing(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var folder = fixture.Combine(fixture.Directory, "nope-folder");
        var file = fixture.Combine(fixture.Directory, "nope-file");
        var created = await fixture.Provider.CreateFolderAsync(folder, cts.Token);
        var written = await fixture.Provider.OpenWriteAsync(file, cts.Token);
        var renamed = await fixture.Provider.RenameAsync(fixture.SampleFilePath, "renamed.txt", cts.Token);
        var deleted = await fixture.Provider.DeleteAsync(fixture.SampleFilePath, cts.Token);
        AssertKind(created.Error, ErrorKind.Cancelled, folder);
        AssertKind(written.Error, ErrorKind.Cancelled, file);
        AssertKind(renamed.Error, ErrorKind.Cancelled, fixture.SampleFilePath);
        AssertKind(deleted.Error, ErrorKind.Cancelled, fixture.SampleFilePath);
        Assert.Equal(fixture.SampleBytes, await ReadAll(fixture.Provider, fixture.SampleFilePath));
        AssertKind(
            (await fixture.Provider.GetEntryAsync(folder, CancellationToken.None)).Error,
            ErrorKind.NotFound,
            folder);
        AssertKind(
            (await fixture.Provider.GetEntryAsync(file, CancellationToken.None)).Error,
            ErrorKind.NotFound,
            file);
    }

    private static async Task Write(IFileSystemProvider provider, string path, byte[] payload)
    {
        var opened = await provider.OpenWriteAsync(path, CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        await using var stream = opened.Value;
        Assert.True(stream.CanWrite);
        await stream.WriteAsync(payload);
    }
}
