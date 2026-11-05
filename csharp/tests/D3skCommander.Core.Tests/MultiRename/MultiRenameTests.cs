using D3skCommander.Core.MultiRename;
using Xunit;
using Engine = D3skCommander.Core.MultiRename.MultiRename;

namespace D3skCommander.Core.Tests.MultiRename;

/// <summary>P22: all 24 tests of v0.1 <c>src/core/multirename.rs</c>.</summary>
public class MultiRenameTests
{
    private static RenameRule Rule(string name, string ext) => new() { NameMask = name, ExtMask = ext };

    private static RenameRule Search(string search, string replace, bool useRegex) =>
        new() { Search = search, Replace = replace, UseRegex = useRegex };

    private static string Name(string file, int index, RenameRule rule) => Engine.NewName(file, index, rule);

    private static RenamePreview Pair(string from, string to) => new(from, to, null);

    [Fact]
    public void NameAndExtensionPlaceholders()
    {
        Assert.Equal("img_photo.JPG", Name("/x/photo.JPG", 0, Rule("img_[N]", "[E]")));
        Assert.Equal("photo", Name("/x/photo.jpg", 0, Rule("[N]", "")));
        Assert.Equal("photo.bak", Name("/x/photo.jpg", 0, Rule("[N]", "bak")));
        Assert.Equal("a.tar.gz", Name("/x/a.tar.gz", 0, new RenameRule()));
        Assert.Equal("noext", Name("/x/noext", 0, new RenameRule()));
    }

    [Fact]
    public void CharRangesAreOneBasedAndInclusive()
    {
        var rule = Rule("[N3]-[N2-4]-[N3-]-[N9]", "[E1]");
        Assert.Equal("c-bcd-cdef-.x", Name("/x/abcdef.xyz", 0, rule));
        Assert.Equal("bc", Name("/x/abc.q", 0, Rule("[N2-99]", "")));
    }

    [Fact]
    public void CharRangesCountUnicodeCharsNotBytes()
    {
        var rule = Rule("[N2]|[N2-3]|[N3-]", "[E2-]");
        Assert.Equal("é|él|llo→wörld.本語", Name("/x/héllo→wörld.日本語", 0, rule));
    }

    [Fact]
    public void InvalidRangesAndPlaceholdersAreErrors()
    {
        foreach (var mask in new[] { "[N0]", "[N5-2]", "[Nx]", "[N-3]", "[Q]", "[n]", "[N", "[C1+x]", "[Cabc]" })
        {
            Assert.False(Engine.TryNewName("/x/a.txt", 0, Rule(mask, "[E]"), out _, out _), mask);
        }
    }

    [Fact]
    public void LiteralBracket()
    {
        Assert.Equal("[a].txt", Name("/x/a.txt", 0, Rule("[[[N]]", "[E]")));
        Assert.Equal("x[y.txt", Name("/x/a.txt", 0, Rule("x[[y", "[E]")));
    }

    [Fact]
    public void CounterDefaultPaddingStepAndInlineForm()
    {
        var rule = Rule("f[C]", "[E]");
        rule.CounterStart = 5;
        rule.CounterStep = 2;
        rule.CounterDigits = 3;
        Assert.Equal("f005.txt", Name("/x/a.txt", 0, rule));
        Assert.Equal("f011.txt", Name("/x/a.txt", 3, rule));
        var inline = Rule("[C10+5:3]_[C7]_[C+3]_[C:2]_[C2+:4]", "");
        Assert.Equal("020_9_7_03_0004", Name("/x/a", 2, inline));
        var negative = Rule("[C]", "");
        negative.CounterStart = 10;
        negative.CounterStep = -4;
        negative.CounterDigits = 3;
        Assert.Equal("-02", Name("/x/a", 3, negative));
        Assert.Equal("-7", Name("/x/a", 1, Rule("[C-5+-2]", "")));
    }

    [Fact]
    public void ParentAndGrandparent()
    {
        var rule = Rule("[G]_[P]_[N]", "[E]");
        Assert.Equal("mid_low_a.txt", Name("/top/mid/low/a.txt", 0, rule));
        Assert.Equal("__a.txt", Name("a.txt", 0, rule));
    }

    [Fact]
    public void DatePlaceholdersUseLocalModificationTime()
    {
        using var dir = new RenameWorkDir();
        var file = dir.Touch("a.txt");
        File.SetLastWriteTime(file, new DateTime(2023, 7, 4, 9, 5, 3, DateTimeKind.Local));
        var rule = Rule("[Y]-[M]-[D] [h].[m].[s]|[YMD]|[hms]", "[E]");
        Assert.Equal("2023-07-04 09.05.03|20230704|090503.txt", Name(file, 0, rule));
        Assert.False(Engine.TryNewName(dir.PathOf("missing"), 0, rule, out _, out _));
    }

    [Fact]
    public void LiteralSearchReplace()
    {
        var rule = Search(".txt", "$1.md", false);
        Assert.Equal("atxt$1.md", Name("/x/atxt.txt", 0, rule));
        rule = Search("A", "b", false);
        Assert.Equal("aba.txt", Name("/x/aAa.txt", 0, rule));
        rule.CaseSensitive = false;
        Assert.Equal("bbb.txt", Name("/x/aAa.txt", 0, rule));
    }

    [Fact]
    public void RegexReplaceWithCaptureGroups()
    {
        var rule = Search(@"^(\w+)_(\d+)", "$2-$1", true);
        Assert.Equal("042-img.jpg", Name("/x/img_042.jpg", 0, rule));
        Assert.False(Engine.TryNewName("/x/a", 0, Search("(", "", true), out _, out _));
        var insensitive = Search("IMG", "p", true);
        insensitive.CaseSensitive = false;
        Assert.Equal("p1.jpg", Name("/x/img1.jpg", 0, insensitive));
    }

    [Fact]
    public void SearchRunsOnFullNameAfterMasksAndBeforeCase()
    {
        var rule = Search("X.TXT", "-end.dat", false);
        rule.NameMask = "[N]x";
        rule.CaseSensitive = false;
        rule.CaseMode = CaseMode.Upper;
        Assert.Equal("A-END.DAT", Name("/x/a.txt", 0, rule));
        Assert.Equal("a", Name("/x/a.txt", 0, Rule("[N]", "")));
    }

    [Fact]
    public void CaseModes()
    {
        Assert.Equal("hello world.txt", Cased("/x/Hello wORLD.TxT", CaseMode.Lower));
        Assert.Equal("HELLO WORLD.TXT", Cased("/x/Hello wORLD.TxT", CaseMode.Upper));
        Assert.Equal("Hello world.txt", Cased("/x/hELLO wORLD.TxT", CaseMode.FirstUpper));
        Assert.Equal("Hello World-It's_É.Txt", Cased("/x/hELLO wORLD-it's_é.TxT", CaseMode.TitleCase));
        Assert.Equal("école.txt", Cased("/x/ÉCOLE.txt", CaseMode.Lower));
    }

    [Fact]
    public void PreviewBuildsTargetsInTheSameDirectory()
    {
        using var dir = new RenameWorkDir();
        var a = dir.Touch("a.txt");
        var b = dir.Touch("b.txt");
        var previews = Engine.Preview([a, b], Rule("n[C:2]", "[E]"));
        Assert.Equal(dir.PathOf("n01.txt"), previews[0].To);
        Assert.Equal(dir.PathOf("n02.txt"), previews[1].To);
        Assert.All(previews, item => Assert.Null(item.Error));
        var same = Engine.Preview([a], new RenameRule());
        Assert.Null(same[0].Error);
        Assert.Equal(a, same[0].To);
    }

    [Fact]
    public void PreviewFlagsDuplicatesInBatch()
    {
        using var dir = new RenameWorkDir();
        var files = new[] { dir.Touch("a.txt"), dir.Touch("b.txt"), dir.Touch("c.txt") };
        var previews = Engine.Preview(files, Rule("same", "[E]"));
        Assert.All(previews, item => Assert.NotNull(item.Error));
        previews = Engine.Preview(files, Rule("c", "txt"));
        Assert.All(previews, item => Assert.NotNull(item.Error));
    }

    [Fact]
    public void PreviewFlagsExistingTargetOutsideBatchButNotInside()
    {
        using var dir = new RenameWorkDir();
        var a = dir.Touch("a.txt");
        dir.Touch("z.txt");
        var outside = Engine.Preview([a], Search("a", "z", false));
        Assert.Contains("exists", outside[0].Error, StringComparison.Ordinal);
        var shift = Engine.Preview([dir.Touch("1.txt"), dir.Touch("2.txt")], Rule("[C2]", "[E]"));
        Assert.All(shift, item => Assert.Null(item.Error));
    }

    [Fact]
    public void PreviewFlagsInvalidNamesAndBadMasks()
    {
        using var dir = new RenameWorkDir();
        var files = new[] { dir.Touch("a.txt") };
        foreach (var (name, ext) in new[] { ("", ""), ("a:b", "[E]"), ("a?", "[E]"), ("a|b", "[E]"), ("a ", ""), ("a.", ""), ("a\"", "") })
        {
            var preview = Engine.Preview(files, Rule(name, ext));
            Assert.NotNull(preview[0].Error);
        }

        var bad = Engine.Preview(files, Rule("[Q]", ""));
        Assert.Contains("unknown placeholder", bad[0].Error, StringComparison.Ordinal);
        Assert.Equal(files[0], bad[0].To);
    }

    [Fact]
    public void ApplyRenamesAndUndoRestores()
    {
        using var dir = new RenameWorkDir();
        var a = dir.Touch("a.txt");
        var b = dir.Touch("b.txt");
        var unchanged = dir.Touch("keep.txt");
        var previews = Engine.Preview([a, b, unchanged], Rule("f[C]", "[E]")).Take(2).ToArray();
        var done = Engine.Apply(previews);
        Assert.Null(done.Error);
        Assert.Equal(2, done.Done.Count);
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.Equal("a.txt", File.ReadAllText(dir.PathOf("f1.txt")));
        Assert.Null(Engine.Undo(done.Done));
        Assert.Equal("a.txt", File.ReadAllText(a));
        Assert.Equal("b.txt", File.ReadAllText(b));
        Assert.False(File.Exists(dir.PathOf("f1.txt")));
    }

    [Fact]
    public void ApplySkipsUnchangedNames()
    {
        using var dir = new RenameWorkDir();
        var a = dir.Touch("a.txt");
        var done = Engine.Apply(Engine.Preview([a], new RenameRule()));
        Assert.Null(done.Error);
        Assert.Empty(done.Done);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void ApplySwapsTwoFiles()
    {
        using var dir = new RenameWorkDir();
        var a = dir.Touch("a");
        var b = dir.Touch("b");
        var done = Engine.Apply([Pair(a, b), Pair(b, a)]);
        Assert.Null(done.Error);
        Assert.Equal("b", File.ReadAllText(a));
        Assert.Equal("a", File.ReadAllText(b));
        Assert.Equal(2, Directory.GetFileSystemEntries(dir.Root).Length);
        Assert.Null(Engine.Undo(done.Done));
        Assert.Equal("a", File.ReadAllText(a));
        Assert.Equal("b", File.ReadAllText(b));
    }

    [Fact]
    public void ApplyHandlesAChainThroughThePreview()
    {
        using var dir = new RenameWorkDir();
        var files = new[] { dir.Touch("1.txt"), dir.Touch("2.txt"), dir.Touch("3.txt") };
        var previews = Engine.Preview(files, Rule("[C2]", "[E]"));
        Assert.All(previews, item => Assert.Null(item.Error));
        var done = Engine.Apply(previews);
        Assert.Null(done.Error);
        Assert.False(File.Exists(dir.PathOf("1.txt")));
        Assert.Equal("1.txt", File.ReadAllText(dir.PathOf("2.txt")));
        Assert.Equal("2.txt", File.ReadAllText(dir.PathOf("3.txt")));
        Assert.Equal("3.txt", File.ReadAllText(dir.PathOf("4.txt")));
        Assert.Equal(3, Directory.GetFileSystemEntries(dir.Root).Length);
    }

    [Fact]
    public void ApplyRollsBackOnError()
    {
        using var dir = new RenameWorkDir();
        var a = dir.Touch("a");
        var b = dir.Touch("b");
        var c = dir.Touch("c");
        var missing = dir.PathOf("missing");
        var failed = Engine.Apply(
        [
            Pair(a, dir.PathOf("a2")),
            Pair(b, dir.PathOf("b2")),
            Pair(missing, dir.PathOf("m2")),
            Pair(c, dir.PathOf("c2")),
        ]);
        Assert.NotNull(failed.Error);
        Assert.Contains("rolled back", failed.Error, StringComparison.Ordinal);
        Assert.Equal("a", File.ReadAllText(a));
        Assert.Equal("b", File.ReadAllText(b));
        Assert.Equal("c", File.ReadAllText(c));
        Assert.Equal(3, Directory.GetFileSystemEntries(dir.Root).Length);
    }

    [Fact]
    public void ApplyNeverOverwritesAndRollsBack()
    {
        using var dir = new RenameWorkDir();
        var a = dir.Touch("a");
        var b = dir.Touch("b");
        var z = dir.Touch("z");
        var failed = Engine.Apply([Pair(a, dir.PathOf("a2")), Pair(b, z)]);
        Assert.NotNull(failed.Error);
        Assert.Contains("already exists", failed.Error, StringComparison.Ordinal);
        Assert.Equal("a", File.ReadAllText(a));
        Assert.Equal("b", File.ReadAllText(b));
        Assert.Equal("z", File.ReadAllText(z));
    }

    [Fact]
    public void ApplyRefusesPreviewsWithErrors()
    {
        using var dir = new RenameWorkDir();
        var files = new[] { dir.Touch("a.txt"), dir.Touch("b.txt") };
        var bad = Engine.Preview(files, Rule("same", "[E]"));
        var failed = Engine.Apply(bad);
        Assert.NotNull(failed.Error);
        Assert.All(files, file => Assert.True(File.Exists(file)));
    }

    [Fact]
    public void RenamesDirectoriesToo()
    {
        using var dir = new RenameWorkDir();
        var folder = dir.PathOf("old");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inner"), "");
        var done = Engine.Apply(Engine.Preview([folder], Search("old", "new", false)));
        Assert.Null(done.Error);
        Assert.True(File.Exists(dir.PathOf(Path.Combine("new", "inner"))));
    }

    private static string Cased(string file, CaseMode mode)
    {
        var rule = new RenameRule { CaseMode = mode };
        return Name(file, 0, rule);
    }
}
