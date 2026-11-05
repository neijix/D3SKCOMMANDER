using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal(new WindowSize(1100, 700), host.Window.StartSize);
        Assert.Equal(new Size(1100, 700), host.Window.ClientSize);
    }

    [AvaloniaFact]
    public void TheShellRendersOneHeadlessFrame()
    {
        using var host = ShellHost.Open(Language.En);
        host.Window.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 700));
        bitmap.Render(host.Window);
        Assert.Equal(new PixelSize(1100, 700), bitmap.PixelSize);
    }

    [AvaloniaFact]
    public void ABoundKeyDispatchesItsCommand()
    {
        var copies = 0;
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IShellModule>(new CopyProbe(() => copies++)));
        host.Window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
        Assert.Equal(1, copies);
        host.Window.KeyPress(Key.F4, RawInputModifiers.Alt, PhysicalKey.F4, null);
        Assert.False(host.Window.IsVisible);
    }

    private sealed class CopyProbe(Action copy) : IShellModule
    {
        public void Contribute(IShellContributions shell) => shell.Handle(CommandId.Copy, copy);
    }
}
