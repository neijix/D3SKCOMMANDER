using D3skCommander.Core.Common;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Providers;

public partial class ProviderContractTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EntryInfoMatchesThePreparedFile(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var read = await fixture.Provider.GetEntryAsync(fixture.SampleFilePath, CancellationToken.None);
        Assert.True(read.IsSuccess, read.Error?.Message);
        Assert.Equal(fixture.SampleFileName, read.Value.Name);
        Assert.Equal(fixture.SampleFilePath, read.Value.Path);
        Assert.False(read.Value.IsDirectory);
        Assert.Equal((ulong)fixture.SampleBytes.Length, read.Value.Size);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EntryInfoOfTheDirectorySaysItIsAFolder(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var info = await fixture.Provider.GetEntryAsync(fixture.Directory, CancellationToken.None);
        Assert.True(info.IsSuccess, info.Error?.Message);
        Assert.True(info.Value.IsDirectory);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task MissingEntryIsNotFound(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var missing = fixture.Combine(fixture.Directory, "missing.txt");
        var read = await fixture.Provider.GetEntryAsync(missing, CancellationToken.None);
        AssertKind(read.Error, ErrorKind.NotFound, missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CancelledEntryInfoIsATypedError(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var missing = fixture.Combine(fixture.Directory, "missing.txt");
        var read = await fixture.Provider.GetEntryAsync(missing, cts.Token);
        AssertKind(read.Error, ErrorKind.Cancelled, missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReadStreamReturnsThePreparedBytes(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var bytes = await ReadAll(fixture.Provider, fixture.SampleFilePath);
        Assert.Equal(fixture.SampleBytes, bytes);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReadOfAMissingFileIsNotFound(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var missing = fixture.Combine(fixture.Directory, "missing.txt");
        var opened = await fixture.Provider.OpenReadAsync(missing, CancellationToken.None);
        AssertKind(opened.Error, ErrorKind.NotFound, missing);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReadOfADirectoryIsATypedError(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        var opened = await fixture.Provider.OpenReadAsync(fixture.Directory, CancellationToken.None);
        AssertKind(opened.Error, ErrorKind.InvalidData, fixture.Directory);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CancelledReadIsATypedError(IProviderCase providerCase)
    {
        using var fixture = providerCase.Open();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var missing = fixture.Combine(fixture.Directory, "missing.txt");
        var opened = await fixture.Provider.OpenReadAsync(missing, cts.Token);
        AssertKind(opened.Error, ErrorKind.Cancelled, missing);
    }
}
