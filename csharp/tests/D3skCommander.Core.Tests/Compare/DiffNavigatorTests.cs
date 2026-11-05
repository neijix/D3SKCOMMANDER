using D3skCommander.Core.Compare;
using Xunit;

namespace D3skCommander.Core.Tests.Compare;

/// <summary>P24: all 3 tests of <c>src/ui/compare_view/diff_view.rs</c>.</summary>
public class DiffNavigatorTests
{
    [Fact]
    public void CountsAndLocatesBlocks()
    {
        var rows = TextCompare.DiffText("a\nb\nc\nd\ne\n", "a\nX\nc\nd\nY\nZ\n", DiffOptions.Default);
        Assert.Equal(2, DiffNavigator.BlockCount(rows));
        Assert.Equal(new BlockSpan(1, 2), DiffNavigator.BlockRange(rows, 1));
        var last = DiffNavigator.BlockRange(rows, rows.Count - 1);
        Assert.Equal(rows.Count, last.End);
        Assert.Equal(new BlockSpan(0, 0), DiffNavigator.BlockRange(rows, 0));
        Assert.Equal(0, DiffNavigator.BlockCount([]));
    }

    [Fact]
    public void PeekStartsTwoRowsBeforeTheDifference()
    {
        Assert.Equal(0, DiffNavigator.PeekStart(0));
        Assert.Equal(0, DiffNavigator.PeekStart(31));
        Assert.Equal(16 * 3, DiffNavigator.PeekStart((16 * 5) + 3));
    }

    [Fact]
    public void ReadsPeekFromBothFiles()
    {
        using var dir = new TempFolder();
        var left = Path.Combine(dir.Path, "a");
        var right = Path.Combine(dir.Path, "b");
        var leftBytes = new byte[500];
        Array.Fill(leftBytes, (byte)1);
        var rightBytes = (byte[])leftBytes.Clone();
        leftBytes[300] = 9;
        File.WriteAllBytes(left, leftBytes);
        File.WriteAllBytes(right, rightBytes);

        var peek = DiffNavigator.ReadPeek(left, right, 300);
        Assert.Equal(256, peek.Start);
        Assert.Equal(128, peek.Left.Length);
        Assert.Equal(9, peek.Left[300 - 256]);
        Assert.Equal(1, peek.Right[300 - 256]);
    }
}
