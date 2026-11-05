using Avalonia.Headless.XUnit;
using D3skCommander.App.Host;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class MainWindowTests
{
    [AvaloniaFact]
    public void EmptyMainWindowOpens()
    {
        using var host = ShellHost.Open(Language.En);
        Assert.True(host.Window.IsVisible);
        Assert.Equal("D3SK Commander", host.Window.Title);
        Assert.Equal(host.Localizer.Get("shell.window_title"), host.Window.Title);
        Assert.Equal("Ready", host.Window.StatusText);
        Assert.NotNull(host.Window.LeftPanel);
        Assert.NotNull(host.Window.RightPanel);
    }

    [AvaloniaFact]
    public void TheShellDrawsOneImGuiFrame()
    {
        using var host = ShellHost.Open(Language.En);
        ImGuiProbe.Draw(host.Window.Draw);
    }
}
