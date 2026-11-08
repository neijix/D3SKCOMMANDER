using D3skCommander.Core.Update;
using Xunit;

namespace D3skCommander.Core.Tests.Update;

public class RelaunchCommandLineTests
{
    [Fact]
    public void FinalInstallFolderArgumentStaysUnquotedAndLast()
    {
        var relaunch = new Relaunch(@"C:\setup.exe", ["/S", "/R", @"/D=C:\Program Files\D3SKCOMMANDER"]);
        Assert.Equal(@"/S /R /D=C:\Program Files\D3SKCOMMANDER", RelaunchCommandLine.Build(relaunch));
    }

    [Fact]
    public void AnArgumentWithASpaceOrQuoteIsQuoted()
    {
        var relaunch = new Relaunch("app", ["plain", "two words", "say \"hi\""]);
        Assert.Equal("plain \"two words\" \"say \\\"hi\\\"\"", RelaunchCommandLine.Build(relaunch));
    }

    [Fact]
    public void NoArgumentsGiveAnEmptyLine()
    {
        Assert.Equal(string.Empty, RelaunchCommandLine.Build(Relaunch.Of("app")));
    }
}
