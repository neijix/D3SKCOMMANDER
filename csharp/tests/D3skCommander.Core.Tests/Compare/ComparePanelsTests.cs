using D3skCommander.Core.Compare;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.Core.Tests.Compare;

/// <summary>
/// P24: Shift+F2 marks, in both panels, each file the other side is missing or that is newer there
/// by more than two seconds. A previous selection is replaced. Directories are not marked.
/// </summary>
public class ComparePanelsTests
{
    [Fact]
    public void MarksFilesMissingOrNewerThanTheOtherSide()
    {
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var leftDir = File("/left/folder", time, directory: true);
        var rightDir = File("/right/folder", time, directory: true);
        var onlyLeft = File("/left/only-left.txt", time);
        var onlyRight = File("/right/only-right.txt", time);
        var newer = File("/left/same.txt", time.AddSeconds(3));
        var older = File("/right/same.txt", time);
        var closeLeft = File("/left/close.txt", time.AddSeconds(ComparePanels.NewerSlackSeconds));
        var closeRight = File("/right/close.txt", time);

        var leftSelection = new Selection();
        var rightSelection = new Selection();
        leftSelection.Add("/left/old");
        rightSelection.Add(leftDir.Path);

        var counts = ComparePanels.Mark(
            [leftDir, onlyLeft, newer, closeLeft],
            leftSelection,
            [rightDir, onlyRight, older, closeRight],
            rightSelection);

        Assert.Equal(new ComparePanelCounts(2, 1), counts);
        Assert.Equal(2, leftSelection.Count);
        Assert.True(leftSelection.Contains(onlyLeft.Path));
        Assert.True(leftSelection.Contains(newer.Path));
        Assert.False(leftSelection.Contains(closeLeft.Path));
        Assert.False(leftSelection.Contains("/left/old"));
        Assert.False(leftSelection.Contains(leftDir.Path));
        Assert.Equal(1, rightSelection.Count);
        Assert.True(rightSelection.Contains(onlyRight.Path));
        Assert.False(rightSelection.Contains(older.Path));
        Assert.False(rightSelection.Contains(closeRight.Path));
    }

    [Fact]
    public void TreatsAMissingTimeAsOlder()
    {
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var undated = File("/left/a.txt", null);
        var dated = File("/right/a.txt", time);
        var neitherLeft = File("/left/b.txt", null);
        var neitherRight = File("/right/b.txt", null);
        var leftSelection = new Selection();
        var rightSelection = new Selection();

        ComparePanels.Mark(
            [undated, neitherLeft],
            leftSelection,
            [dated, neitherRight],
            rightSelection);

        Assert.False(leftSelection.Contains(undated.Path));
        Assert.True(rightSelection.Contains(dated.Path));
        Assert.False(leftSelection.Contains(neitherLeft.Path));
        Assert.False(rightSelection.Contains(neitherRight.Path));
    }

    private static Entry File(string path, DateTimeOffset? modified, bool directory = false)
    {
        var name = Path.GetFileName(path);
        return new Entry(name, path, directory, false, false, directory ? 0ul : 1ul, modified, false, false, "");
    }
}
