using D3skCommander.Core.FileOps;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public class FileClipboardTests
{
    [Theory]
    [InlineData("a.txt", false, "a (2).txt")]
    [InlineData("a (2).txt", false, "a (3).txt")]
    [InlineData("a (x).txt", false, "a (x) (2).txt")]
    [InlineData("README", false, "README (2)")]
    [InlineData(".env", false, ".env (2)")]
    [InlineData("v1.2", true, "v1.2 (2)")]
    [InlineData("photo.tar.gz", false, "photo.tar (2).gz")]
    public void TheNextNameNumbersTheStem(string name, bool folder, string next) =>
        Assert.Equal(next, FileClipboard.NextFreeName(name, folder, _ => false));

    [Fact]
    public void TakenNamesAreSkipped()
    {
        var taken = new HashSet<string> { "a (2).txt", "a (3).txt" };

        Assert.Equal("a (4).txt", FileClipboard.NextFreeName("a.txt", false, taken.Contains));
    }

    [Fact]
    public void APasteSplitsSameFolderCopiesFromTheRest()
    {
        var here = Path.Combine(Path.GetTempPath(), "here");
        var there = Path.Combine(Path.GetTempPath(), "there");
        var clipboard = new FileClipboard();
        clipboard.Set([Path.Combine(here, "a.txt"), Path.Combine(here, "b.txt"), Path.Combine(there, "c.txt")], cut: false);

        var plan = clipboard.Plan(here, path => path.EndsWith("b (2).txt", StringComparison.Ordinal), _ => false);

        Assert.False(plan.Move);
        Assert.Equal([Path.Combine(there, "c.txt")], plan.Into);
        Assert.Equal(
            [new PasteCopy(Path.Combine(here, "a.txt"), Path.Combine(here, "a (2).txt")),
             new PasteCopy(Path.Combine(here, "b.txt"), Path.Combine(here, "b (3).txt"))],
            plan.Duplicates);
    }

    [Fact]
    public void ACutIntoItsOwnFolderDoesNothingAndIsForgottenOncePasted()
    {
        var here = Path.Combine(Path.GetTempPath(), "here");
        var clipboard = new FileClipboard();
        clipboard.Set([Path.Combine(here, "a.txt")], cut: true);

        Assert.True(clipboard.Plan(here, _ => false, _ => false).IsEmpty);
        var elsewhere = clipboard.Plan(Path.GetTempPath(), _ => false, _ => false);
        Assert.True(elsewhere.Move);
        Assert.Single(elsewhere.Into);

        clipboard.Pasted();
        Assert.True(clipboard.IsEmpty);
        clipboard.Set([Path.Combine(here, "a.txt")], cut: false);
        clipboard.Pasted();
        Assert.False(clipboard.IsEmpty);
    }
}
