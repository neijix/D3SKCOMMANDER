using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
        Assert.Equal("Ready", host.Window.FindControl<TextBlock>("StatusLine")!.Text);
        Assert.NotNull(host.Window.FindControl<ContentControl>("LeftPanel"));
        Assert.NotNull(host.Window.FindControl<ContentControl>("RightPanel"));
    }
}
