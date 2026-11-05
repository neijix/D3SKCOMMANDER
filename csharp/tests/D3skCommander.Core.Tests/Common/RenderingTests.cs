using D3skCommander.Core.Common;
using Xunit;

namespace D3skCommander.Core.Tests.Common;

public class RenderingTests
{
    [Theory]
    [InlineData("software")]
    [InlineData("Software")]
    [InlineData(" SOFTWARE ")]
    public void SoftwareInAnyCaseForcesCpuDrawing(string value) =>
        Assert.Equal(RenderingChoice.Software, Rendering.Parse(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpu")]
    [InlineData("auto")]
    [InlineData("soft")]
    public void AnythingElseLeavesThePlatformChoice(string? value) =>
        Assert.Equal(RenderingChoice.Auto, Rendering.Parse(value));
}
