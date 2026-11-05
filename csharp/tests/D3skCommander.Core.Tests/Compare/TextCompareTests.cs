using D3skCommander.Core.Compare;
using Xunit;

namespace D3skCommander.Core.Tests.Compare;

/// <summary>P24: all 10 tests of <c>src/core/compare.rs</c>.</summary>
public class TextCompareTests
{
    [Fact]
    public void EqualTexts()
    {
        var rows = TextCompare.DiffText("a\nb\n", "a\nb\n", DiffOptions.Default);
        Assert.Equal([RowKind.Equal, RowKind.Equal], Kinds(rows));
        Assert.Equal(2, rows[1].LeftNumber);
        Assert.Equal(2, rows[1].RightNumber);
        Assert.Equal("b", rows[1].Left);
        Assert.Empty(TextCompare.DiffText("", "", DiffOptions.Default));
    }

    [Fact]
    public void InsertAndDelete()
    {
        var inserted = TextCompare.DiffText("a\nc\n", "a\nb\nc\n", DiffOptions.Default);
        Assert.Equal([RowKind.Equal, RowKind.RightOnly, RowKind.Equal], Kinds(inserted));
        Assert.Null(inserted[1].LeftNumber);
        Assert.Equal(2, inserted[1].RightNumber);
        Assert.Equal(2, inserted[2].LeftNumber);
        Assert.Equal(3, inserted[2].RightNumber);

        var deleted = TextCompare.DiffText("a\nb\nc\n", "a\nc\n", DiffOptions.Default);
        Assert.Equal([RowKind.Equal, RowKind.LeftOnly, RowKind.Equal], Kinds(deleted));
        Assert.Equal("b", deleted[1].Left);
        Assert.Null(deleted[1].Right);
        Assert.Equal(2, deleted[2].RightNumber);
    }

    [Fact]
    public void ChangePairsLinesAndKeepsSurplus()
    {
        var rows = TextCompare.DiffText("x\nold1\nold2\nold3\ny\n", "x\nnew1\nnew2\ny\n", DiffOptions.Default);
        Assert.Equal(
            [RowKind.Equal, RowKind.Changed, RowKind.Changed, RowKind.LeftOnly, RowKind.Equal],
            Kinds(rows));
        Assert.Equal("old1", rows[1].Left);
        Assert.Equal("new1", rows[1].Right);
        Assert.Equal((3, 3), (rows[2].LeftNumber, rows[2].RightNumber));
        Assert.Equal((4, null), (rows[3].LeftNumber, rows[3].RightNumber));
        Assert.Equal((5, 4), (rows[4].LeftNumber, rows[4].RightNumber));

        var shorter = TextCompare.DiffText("a\n", "b\nc\n", DiffOptions.Default);
        Assert.Equal([RowKind.Changed, RowKind.RightOnly], Kinds(shorter));
    }

    [Fact]
    public void IgnoreCaseAndWhitespace()
    {
        var strict = TextCompare.DiffText("Hello  World\n", "hello world\n", DiffOptions.Default);
        Assert.Equal([RowKind.Changed], Kinds(strict));

        var caseFolded = TextCompare.DiffText("Hello World\n", "hello world\n", new DiffOptions(IgnoreCase: true, IgnoreWhitespace: false));
        Assert.Equal([RowKind.Equal], Kinds(caseFolded));
        Assert.Equal("Hello World", caseFolded[0].Left);
        Assert.Equal("hello world", caseFolded[0].Right);

        var whitespace = new DiffOptions(IgnoreCase: false, IgnoreWhitespace: true);
        Assert.Equal([RowKind.Equal], Kinds(TextCompare.DiffText("a \t b\n", "ab\n", whitespace)));
        Assert.Equal([RowKind.Changed], Kinds(TextCompare.DiffText("a b\n", "AB\n", whitespace)));
    }

    [Fact]
    public void CrlfLinesCompareEqual()
    {
        var rows = TextCompare.DiffText("a\r\nb\r\n", "a\nb\n", DiffOptions.Default);
        Assert.Equal([RowKind.Equal, RowKind.Equal], Kinds(rows));
    }

    [Fact]
    public void BinaryDetectionAndFirstDifference()
    {
        using var dir = new TempFolder();
        var left = Path.Combine(dir.Path, "a.bin");
        var right = Path.Combine(dir.Path, "b.bin");
        File.WriteAllBytes(left, [1, 2, 0, 4, 5]);
        File.WriteAllBytes(right, [1, 2, 0, 9, 5]);
        AssertBinary(FileCompare.DiffFiles(left, right, DiffOptions.Default), identical: false, first: 3);

        File.WriteAllBytes(right, [1, 2, 0, 4, 5]);
        AssertBinary(FileCompare.DiffFiles(left, right, DiffOptions.Default), identical: true, first: null);

        File.WriteAllBytes(right, [1, 2, 0]);
        AssertBinary(FileCompare.DiffFiles(left, right, DiffOptions.Default), identical: false, first: 3);

        File.WriteAllBytes(right, "text"u8.ToArray());
        Assert.False(FileCompare.DiffFiles(left, right, DiffOptions.Default).IsText);
    }

    [Fact]
    public void FirstDifferenceCrossesChunks()
    {
        using var dir = new TempFolder();
        var left = Path.Combine(dir.Path, "a");
        var right = Path.Combine(dir.Path, "b");
        var data = new byte[200_000];
        Array.Fill(data, (byte)7);
        File.WriteAllBytes(left, data);
        data[150_000] = 8;
        File.WriteAllBytes(right, data);

        Assert.False(FileCompare.FilesIdentical(left, right));
        Assert.Equal(150_000, FileCompare.FirstDifference(left, right));
    }

    [Fact]
    public void TextFilesDiffAsText()
    {
        using var dir = new TempFolder();
        var left = Path.Combine(dir.Path, "a.txt");
        var right = Path.Combine(dir.Path, "b.txt");
        File.WriteAllBytes(left, "one\ntwo\n"u8.ToArray());
        File.WriteAllBytes(right, "one\n2\n"u8.ToArray());

        var diff = FileCompare.DiffFiles(left, right, DiffOptions.Default);
        Assert.True(diff.IsText);
        Assert.Equal([RowKind.Equal, RowKind.Changed], Kinds(diff.Rows));
        Assert.True(FileCompare.FilesIdentical(left, left));
        Assert.False(FileCompare.FilesIdentical(left, right));
        Assert.Throws<FileNotFoundException>(() => FileCompare.DiffFiles(left, Path.Combine(dir.Path, "missing"), DiffOptions.Default));
    }

    [Fact]
    public void NextAndPrevDifference()
    {
        var rows = TextCompare.DiffText("a\nb\nc\nd\ne\nf\n", "a\nB\nC\nd\nf\nf2\n", DiffOptions.Default);
        var starts = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (TextCompare.IsBlockStart(rows, i))
            {
                starts.Add(i);
            }
        }

        Assert.True(starts.Count >= 2, "expected at least two difference blocks");
        var first = starts[0];
        Assert.Equal(first, TextCompare.NextDifference(rows, 0));
        Assert.Equal(starts[1], TextCompare.NextDifference(rows, first));
        Assert.Null(TextCompare.NextDifference(rows, starts[^1]));
        Assert.Null(TextCompare.PrevDifference(rows, first));
        Assert.Equal(first, TextCompare.PrevDifference(rows, starts[1]));
        Assert.Null(TextCompare.PrevDifference(rows, first + 1));
        Assert.Equal(starts[^1], TextCompare.PrevDifference(rows, rows.Count + 5));
        Assert.Null(TextCompare.NextDifference([], 0));
        Assert.Null(TextCompare.PrevDifference([], 0));
        Assert.Null(TextCompare.NextDifference(rows, int.MaxValue));
    }

    [Fact]
    public void NoDifferenceInEqualRows()
    {
        var rows = TextCompare.DiffText("a\nb\n", "a\nb\n", DiffOptions.Default);
        Assert.Null(TextCompare.NextDifference(rows, 0));
        Assert.Null(TextCompare.PrevDifference(rows, 1));
    }

    private static List<RowKind> Kinds(IReadOnlyList<DiffRow> rows) => rows.Select(row => row.Kind).ToList();

    private static void AssertBinary(FileDiff diff, bool identical, long? first)
    {
        Assert.False(diff.IsText);
        Assert.Equal(identical, diff.Identical);
        Assert.Equal(first, diff.FirstDifference);
    }
}
