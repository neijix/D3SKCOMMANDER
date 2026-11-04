using D3skCommander.Core.Wildcard;
using Xunit;

namespace D3skCommander.Core.Tests.Wildcard;

public class WildcardTests
{
    [Fact]
    public void IncludeStarExtension()
    {
        var m = new Mask("*.txt");
        Assert.True(m.Matches("a.txt"));
        Assert.True(m.Matches(".txt"));
        Assert.False(m.Matches("a.txt.bak"));
        Assert.False(m.Matches("a.md"));
    }

    [Fact]
    public void MultiMaskSemicolonAndSpace()
    {
        foreach (var p in new[] { "*.txt;*.md", "*.txt *.md", "*.txt; *.md" })
        {
            var m = new Mask(p);
            Assert.True(m.Matches("a.txt"), p);
            Assert.True(m.Matches("b.md"), p);
            Assert.False(m.Matches("c.rs"), p);
        }
    }

    [Fact]
    public void SemicolonKeepsSpacesInNames()
    {
        var m = new Mask("my file.txt;*.md");
        Assert.True(m.Matches("my file.txt"));
        Assert.False(m.Matches("my"));
    }

    [Fact]
    public void ExcludeAfterPipe()
    {
        var m = new Mask("*.rs | test_*;*_old.rs");
        Assert.True(m.Matches("main.rs"));
        Assert.False(m.Matches("test_a.rs"));
        Assert.False(m.Matches("lib_old.rs"));
        Assert.False(m.Matches("main.txt"));
    }

    [Fact]
    public void ExcludeOnlyMeansEverythingElse()
    {
        var m = new Mask("| *.bak");
        Assert.True(m.Matches("a.txt"));
        Assert.False(m.Matches("a.bak"));
        Assert.False(m.IsMatchAll);
    }

    [Fact]
    public void QuestionMarkIsOneChar()
    {
        var m = new Mask("a?c.txt");
        Assert.True(m.Matches("abc.txt"));
        Assert.False(m.Matches("ac.txt"));
        Assert.False(m.Matches("abbc.txt"));
    }

    [Fact]
    public void CaseInsensitive()
    {
        Assert.True(new Mask("*.TXT").Matches("readme.txt"));
        Assert.True(new Mask("README.md").Matches("readme.MD"));
    }

    [Fact]
    public void WithoutWildcardIsExactNotSubstring()
    {
        var m = new Mask("a.txt");
        Assert.True(m.Matches("a.txt"));
        Assert.True(m.Matches("A.TXT"));
        Assert.False(m.Matches("ba.txt"));
        Assert.False(m.Matches("a.txt2"));
        Assert.False(new Mask("note").Matches("notes.txt"));
    }

    [Fact]
    public void MatchAllVariants()
    {
        foreach (var p in new[] { "", "*", "*.*", "  ", "a.txt;*" })
        {
            var m = new Mask(p);
            Assert.True(m.IsMatchAll, p);
            Assert.True(m.Matches("noext"), p);
            Assert.True(m.Matches("x.y"), p);
        }

        Assert.False(new Mask("*.txt").IsMatchAll);
        Assert.False(new Mask("* | *.bak").IsMatchAll);
    }

    [Fact]
    public void StarBacktrackingAndUnicode()
    {
        Assert.True(new Mask("*a*b*c").Matches("xxaxxbxxc"));
        Assert.False(new Mask("*a*b*c").Matches("xxaxxbxx"));
        Assert.True(new Mask("*.tar.gz").Matches("a.tar.gz"));
        Assert.True(new Mask("é?.txt").Matches("ÉA.txt"));
    }
}
