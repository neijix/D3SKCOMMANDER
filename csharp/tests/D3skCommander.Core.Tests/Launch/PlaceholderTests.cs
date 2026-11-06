using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.Core.Tests.Launch;

/// <summary>The placeholders of user commands beyond %s: %p the full path, %f the file name, %d the folder.</summary>
public class PlaceholderTests
{
    [Fact]
    public void PathNameAndFolderPlaceholdersAreExpandedPerFile()
    {
        var commands = CommandBuilder.Build("tool %p --name=%f --in=%d", ["/data/a b.txt", "/data/c.txt"], LaunchMode.OncePerFile);

        Assert.Equal(2, commands.Count);
        var folder = Path.GetDirectoryName("/data/a b.txt");
        Assert.Equal(["/data/a b.txt", "--name=a b.txt", "--in=" + folder], commands[0].Arguments);
        Assert.Equal(["/data/c.txt", "--name=c.txt", "--in=" + folder], commands[1].Arguments);
    }

    [Fact]
    public void AnArgumentWithAPlaceholderRepeatsOncePerFileWhenAllAreRunAtOnce()
    {
        var command = Assert.Single(CommandBuilder.Build("tool -n %f", ["/x/a", "/y/b"], LaunchMode.AllAtOnce));

        Assert.Equal(["-n", "a", "b"], command.Arguments);
    }

    [Fact]
    public void ANewPlaceholderCountsSoTheFilesAreNotAppended()
    {
        var command = Assert.Single(CommandBuilder.Build("tool --dir %d", ["/x/a"], LaunchMode.AllAtOnce));

        Assert.Equal(["--dir", Path.GetDirectoryName("/x/a")!], command.Arguments);
    }

    [Fact]
    public void AnUnknownPercentSequenceIsLeftAlone()
    {
        var command = Assert.Single(CommandBuilder.Build("tool 100%x %", ["/x/a"], LaunchMode.AllAtOnce));

        Assert.Equal(["100%x", "%", "/x/a"], command.Arguments);
    }

    [Fact]
    public void ThePlaceholdersAreNotExpandedInTheProgramWord()
    {
        var command = Assert.Single(CommandBuilder.Build("%p --f=%s", ["/x/a"], LaunchMode.AllAtOnce));

        Assert.Equal("%p", command.Program);
        Assert.Equal(["--f=/x/a"], command.Arguments);
    }

    [Fact]
    public void ExpandPlaceholdersWorksInOnePass()
    {
        Assert.Equal($"a.txt in {Path.GetDirectoryName("/d/a.txt")} (/d/a.txt) %q", CommandBuilder.ExpandPlaceholders("%f in %d (%p) %q", "/d/a.txt"));
        Assert.Equal("plain", CommandBuilder.ExpandPlaceholders("plain", "/d/a.txt"));
        Assert.Equal("%f", CommandBuilder.ExpandPlaceholders("%f", "/d/%f"));
    }
}
