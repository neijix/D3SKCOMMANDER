using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;
using Xunit;

namespace D3skCommander.Core.Tests.NetDrives;

/// <summary>Disk usage on a server root scans its shares.</summary>
public class ServerScanTests
{
    [Fact]
    public void AServerRootGivesItsShareRoots()
    {
        var shares = new FakeShares(FileResult<IReadOnlyList<string>>.Ok(["media", "backup"]));

        var found = ServerScan.ShareRoots("\\\\192.168.1.15\\", shares);

        Assert.NotNull(found);
        var roots = found.Value;
        Assert.True(roots.IsSuccess);
        Assert.Equal(["\\\\192.168.1.15\\media", "\\\\192.168.1.15\\backup"], roots.Value);
        Assert.Equal(["192.168.1.15"], shares.Asked);
    }

    [Fact]
    public void AnyOtherPathIsNotAServerAndAsksNothing()
    {
        var shares = new FakeShares(FileResult<IReadOnlyList<string>>.Ok(["media"]));

        Assert.Null(ServerScan.ShareRoots("\\\\nas\\media", shares));
        Assert.Null(ServerScan.ShareRoots("C:\\", shares));
        Assert.Null(ServerScan.ShareRoots("/home", shares));
        Assert.Empty(shares.Asked);
    }

    [Fact]
    public void AServerThatCannotBeListedGivesItsError()
    {
        var error = OperationError.Unsupported("no network", "\\\\nas\\");
        var found = ServerScan.ShareRoots("\\\\nas", new FakeShares(FileResult<IReadOnlyList<string>>.Fail(error)));

        Assert.NotNull(found);
        var roots = found.Value;
        Assert.False(roots.IsSuccess);
        Assert.Equal(error, roots.Error);
    }

    private sealed class FakeShares(FileResult<IReadOnlyList<string>> answer) : IServerShares
    {
        public List<string> Asked { get; } = [];

        public FileResult<IReadOnlyList<string>> Shares(string server)
        {
            Asked.Add(server);
            return answer;
        }
    }
}
