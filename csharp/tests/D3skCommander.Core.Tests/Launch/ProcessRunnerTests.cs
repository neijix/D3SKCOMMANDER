using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.Core.Tests.Launch;

public class ProcessRunnerTests
{
    [Fact]
    public void WindowsShellStartsTheSystemCmdByFullPath()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the cmd shell is Windows only");
        var info = ProcessRunner.CreateStartInfo(new ProcessRequest.Shell("echo x", Path.GetTempPath()));
        Assert.True(Path.IsPathFullyQualified(info.FileName));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), info.FileName);
    }
}
