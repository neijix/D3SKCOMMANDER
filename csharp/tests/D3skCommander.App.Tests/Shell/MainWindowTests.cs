using Avalonia.Headless.XUnit;
using D3skCommander.App.Shell;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class MainWindowTests
{
    [AvaloniaFact]
    public void EmptyMainWindowOpens()
    {
        var window = new MainWindow();
        window.Show();
        Assert.True(window.IsVisible);
        Assert.Equal("D3SK Commander", window.Title);
        window.Close();
    }
}
