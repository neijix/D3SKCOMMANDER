using D3skCommander.Core.DiskUsage;
using Xunit;

namespace D3skCommander.Core.Tests.DiskUsage;

public class ScanEstimateTests
{
    [Fact]
    public void PercentUsesFilesFirstThenBytesAndStopsShortOfTheEnd()
    {
        Assert.Equal(25, new ScanEstimate(400, 0).Percent(new ScanProgress(100, 0, 999_999)));
        Assert.Equal(50, new ScanEstimate(0, 1000).Percent(new ScanProgress(1, 0, 500)));
        Assert.Equal(99, new ScanEstimate(10, 0).Percent(new ScanProgress(50, 0, 0)));
        Assert.Null(ScanEstimate.None.Percent(new ScanProgress(1, 0, 1)));
        Assert.False(ScanEstimate.None.IsKnown);
    }

    [Fact]
    public void FromDrivesSumsEveryDriveOrReadsTheRootOnly()
    {
        long? Used(string root) => root.Length * 100L;
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(new ScanEstimate(0, 300 + 400), ScanEstimate.FromDrives("computer:", ["abc", "abcd"], Used));
        Assert.Equal(new ScanEstimate(0, root.Length * 100L), ScanEstimate.FromDrives(root, null, Used));
        Assert.Equal(ScanEstimate.None, ScanEstimate.FromDrives(Path.Combine(root, "some", "folder"), null, Used));
        Assert.Equal(ScanEstimate.None, ScanEstimate.FromDrives(root, null, _ => null));
    }
}
