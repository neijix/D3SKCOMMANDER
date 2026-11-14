using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.Core.Tests.Settings;

/// <summary>A path typed for a new favorite: network servers and shares are kept, cleaned and named.</summary>
public class HotlistPathTests
{
    [Theory]
    [InlineData(@"\\192.168.1.15\", @"\\192.168.1.15\")]
    [InlineData(@"\\192.168.1.15", @"\\192.168.1.15\")]
    [InlineData(@"  ""\\192.168.1.15\\"" ", @"\\192.168.1.15\")]
    [InlineData("//nas/", @"\\nas\")]
    [InlineData(@"\\nas\photos", @"\\nas\photos")]
    [InlineData(@"C:\Users", @"C:\Users")]
    [InlineData("/home/me/docs", "/home/me/docs")]
    public void ATypedPathIsCleanedAndAServerBecomesItsRoot(string typed, string expected) =>
        Assert.Equal(expected, HotlistPath.Clean(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData(null)]
    public void NothingTypedGivesNoPath(string? typed) => Assert.Null(HotlistPath.Clean(typed));

    [Theory]
    [InlineData(@"\\192.168.1.15\", "192.168.1.15")]
    [InlineData(@"\\nas\photos", "photos")]
    [InlineData(@"C:\Users\me\", "me")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("/", "/")]
    public void TheDefaultNameIsTheLastPartOfThePath(string path, string expected) =>
        Assert.Equal(expected, HotlistPath.DefaultName(path));
}
