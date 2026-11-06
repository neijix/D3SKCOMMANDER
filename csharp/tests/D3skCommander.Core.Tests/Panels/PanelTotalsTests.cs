using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class PanelTotalsTests
{
    private static Entry File(string name, ulong size) => new(name, "/t/" + name, false, false, false, size, null, false, false, "");

    private static Entry Folder(string name, ulong size = 0) => new(name, "/t/" + name, true, false, false, size, null, false, false, "");

    private static Entry Parent() => new("..", "/", true, false, true, 999, null, false, false, "");

    [Fact]
    public void MarkedAndTotalBytesAreCountedAndTheParentRowIsLeftOut()
    {
        var entries = new[] { Parent(), File("a", 100), File("b", 200), Folder("d", 700) };

        var totals = PanelTotals.Compute(entries, ["/t/b", "/t/d"]);

        Assert.Equal(new PanelTotals(2, 3, 900, 1000), totals);
    }

    [Fact]
    public void AMarkedParentAndUnknownPathsAreIgnoredAndAnUnmeasuredFolderAddsNothing()
    {
        var entries = new[] { Parent(), File("a", 5), Folder("unknown") };

        var totals = PanelTotals.Compute(entries, ["/", "/t/missing", "/t/unknown"]);

        Assert.Equal(new PanelTotals(1, 2, 0, 5), totals);
    }

    [Fact]
    public void SumAndMarkedBytesMatchCompute()
    {
        var entries = new[] { Parent(), File("a", 1), File("b", 2) };

        Assert.Equal(3UL, PanelTotals.SumBytes(entries));
        Assert.Equal(2UL, PanelTotals.MarkedBytes(entries, ["/t/b"]));
        Assert.Equal(0UL, PanelTotals.MarkedBytes(entries, []));
        Assert.Equal(new PanelTotals(0, 0, 0, 0), PanelTotals.Compute([], []));
    }

    [Fact]
    public void FreeSpaceIsKnownForALocalFolderAndNullOtherwise()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-free-").FullName;
        try
        {
            Assert.NotNull(PanelTotals.FreeSpace(folder));
            Assert.Null(PanelTotals.FreeSpace(Path.Combine(folder, "missing", "inside.zip")));
            Assert.Null(PanelTotals.FreeSpace("ftp://host/dir"));
            Assert.Null(PanelTotals.FreeSpace(""));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
