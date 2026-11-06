using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Features.Drives;

public class DriveFlyoutTests
{
    [AvaloniaFact]
    public async Task TheMenuOpensOnThePanelsDriveIconWithSectionsAndReturnsTheClickedLine()
    {
        using var host = ShellHost.Open(Language.En);
        var fallback = new NeverPresenter();
        var presenter = new FlyoutDriveMenuPresenter(new Owner(host.Window), fallback);
        DriveMenuItem[] items =
        [
            new(DriveMenuKind.Volume, "Root (/)", "/", 0.5, true),
            new(DriveMenuKind.Place, "Home", "/home/me", null, false),
            new(DriveMenuKind.Unmounted, "Mount: USB (8 GB)", "/dev/sdb1", null, false),
        ];

        var shown = presenter.ShowAsync(PanelSide.Left, items);
        for (var spin = 0; spin < 50 && presenter.LastMenu is null && !fallback.Used; spin++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.False(fallback.Used);
        var menu = presenter.LastMenu;
        Assert.NotNull(menu);
        Assert.True(menu.IsOpen);
        var lines = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal(2, menu.Items.OfType<Separator>().Count());
        Assert.True(lines[0].IsChecked);
        Assert.IsType<StackPanel>(lines[0].Header);

        lines[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();

        Assert.Same(items[2], await shown);
    }

    private sealed class Owner(MainWindow window) : IDialogOwner
    {
        public MainWindow? Current => window;
    }

    private sealed class NeverPresenter : IDriveMenuPresenter
    {
        public bool Used { get; private set; }

        public Task<DriveMenuItem?> ShowAsync(PanelSide side, IReadOnlyList<DriveMenuItem> items)
        {
            Used = true;
            return Task.FromResult<DriveMenuItem?>(null);
        }
    }
}
