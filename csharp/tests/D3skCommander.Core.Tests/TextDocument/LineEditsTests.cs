using D3skCommander.Core.TextDocument;
using Xunit;

namespace D3skCommander.Core.Tests.TextDocument;

public class LineEditsTests
{
    [Fact]
    public void DuplicateRepeatsTheCaretLineUnderItself()
    {
        var edit = LineEdits.Duplicate("one\ntwo\nthree", 5, 0)!;

        Assert.Equal("one\ntwo\ntwo\nthree", LineEdits.Apply("one\ntwo\nthree", edit));
        Assert.Equal(9, edit.SelectStart);
    }

    [Fact]
    public void DuplicateRepeatsTheSelectionAndKeepsTheWindowsBreaks()
    {
        Assert.Equal("ab\r\nab\r\ncd", Run(LineEdits.Duplicate("ab\r\ncd", 1, 0)!, "ab\r\ncd"));
        Assert.Equal("abbc", Run(LineEdits.Duplicate("abc", 1, 1)!, "abc"));
    }

    [Theory]
    [InlineData("one\ntwo\nthree", 5, 0, "one\nthree")]
    [InlineData("one\ntwo\nthree", 10, 0, "one\ntwo")]
    [InlineData("one\r\ntwo\r\nthree", 0, 0, "two\r\nthree")]
    [InlineData("one\ntwo\nthree", 1, 5, "three")]
    [InlineData("alone", 2, 0, "")]
    public void DeleteLinesRemovesEveryLineTheSelectionTouches(string text, int start, int length, string expected)
    {
        Assert.Equal(expected, Run(LineEdits.DeleteLines(text, start, length)!, text));
    }

    [Fact]
    public void DeleteLinesOnAnEmptyTextDoesNothing()
    {
        Assert.Null(LineEdits.DeleteLines("", 0, 0));
    }

    [Fact]
    public void MoveLinesSwapsWithTheNeighbourAndTheCaretFollows()
    {
        var up = LineEdits.MoveLines("one\ntwo\nthree", 5, 0, up: true)!;
        Assert.Equal("two\none\nthree", LineEdits.Apply("one\ntwo\nthree", up));
        Assert.Equal(1, up.SelectStart);

        var down = LineEdits.MoveLines("one\ntwo\nthree", 5, 0, up: false)!;
        Assert.Equal("one\nthree\ntwo", LineEdits.Apply("one\ntwo\nthree", down));
        Assert.Equal(11, down.SelectStart);
    }

    [Fact]
    public void MoveLinesStopsAtTheEdges()
    {
        Assert.Null(LineEdits.MoveLines("one\ntwo", 1, 0, up: true));
        Assert.Null(LineEdits.MoveLines("one\ntwo", 5, 0, up: false));
    }

    [Fact]
    public void ChangeCaseWorksOnTheSelectionOnly()
    {
        Assert.Equal("aBCd", Run(LineEdits.ChangeCase("abcd", 1, 2, upper: true)!, "abcd"));
        Assert.Equal("Abcd", Run(LineEdits.ChangeCase("ABcd", 1, 1, upper: false)!, "ABcd"));
        Assert.Null(LineEdits.ChangeCase("abcd", 1, 0, upper: true));
    }

    private static string Run(LineEdit edit, string text) => LineEdits.Apply(text, edit);
}
