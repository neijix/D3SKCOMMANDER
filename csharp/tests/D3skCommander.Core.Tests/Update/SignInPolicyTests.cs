using D3skCommander.Core.Update;
using Xunit;

namespace D3skCommander.Core.Tests.Update;

/// <summary>The sign-in policy: a quiet check never starts the device flow; the only URL the app opens is the device page.</summary>
public class SignInPolicyTests
{
    [Fact]
    public void OnlyAPersonsActionMayStartTheDeviceFlow()
    {
        Assert.True(SignInPolicy.MayStartDeviceFlow(userInitiated: true));
        Assert.False(SignInPolicy.MayStartDeviceFlow(userInitiated: false));
    }

    [Fact]
    public void TheDevicePageIsTheOnlyUrlTheAppMayOpen()
    {
        Assert.Equal("https://github.com/login/device", SignInPolicy.VerificationUrl);
        Assert.True(SignInPolicy.MayOpen("https://github.com/login/device"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://github.com/login/device")]
    [InlineData("https://github.com/login/device/")]
    [InlineData("https://github.com/login/device?user_code=X")]
    [InlineData("https://github.com/login/device#x")]
    [InlineData("https://github.com/login/oauth/access_token")]
    [InlineData("https://github.com.evil.example/login/device")]
    [InlineData("https://evil.example/login/device")]
    [InlineData("https://github.com:444/login/device")]
    [InlineData("https://user@github.com/login/device")]
    [InlineData("HTTPS://GITHUB.COM/login/device")]
    [InlineData("https://github.com/login/device ")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://localhost/login/device")]
    public void AnyOtherUrlIsRefused(string? url)
    {
        Assert.False(SignInPolicy.MayOpen(url));
    }
}
