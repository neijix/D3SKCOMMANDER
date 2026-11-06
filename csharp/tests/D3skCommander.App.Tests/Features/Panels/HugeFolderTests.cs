using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Testing;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Features.Panels;

/// <summary>
/// Two panes on a 100k-entry folder: every step the UI thread runs (a beat that binds a new
/// listing, the layout, a key, a sort, select all) stays under 100 ms. A laptop measures 2-50 ms.
/// </summary>
public class HugeFolderTests
{
    private const int Count = 100_000;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(100);

    [AvaloniaFact]
    [Slow]
    public async Task TheUiThreadNeverBlocksOverABudgetOnAHundredThousandEntryFolder()
    {
        var dir = Directory.CreateTempSubdirectory("d3sk-huge-").FullName;
        var text = TextCatalog.Load(Language.En, Language.En);
        using var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker());
        var left = new PanelPaneModel(PanelSide.Left, workspace, text, new FakeDialogService());
        var right = new PanelPaneModel(PanelSide.Right, workspace, text, new FakeDialogService());
        var window = Host(left, right);
        try
        {
            for (var i = 0; i < Count; i++)
            {
                File.Create(Path.Combine(dir, "file" + i + (i % 3 == 0 ? ".txt" : ".dat"))).Dispose();
            }

            window.Show();
            await workspace.OpenAsync(dir, dir, TestContext.Current.CancellationToken);
            OnUiThread("the left beat binding the listing", left.Tick);
            OnUiThread("the right beat binding the listing", right.Tick);
            OnUiThread("the layout", () => Settle(window));
            Assert.Equal(Count + 1, left.List.Items.Count);

            ((Control)window.Content!).Focus();
            OnUiThread("a key", () =>
            {
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
            });
            OnUiThread("the beat after a key", () => Beat(window, left));

            await workspace.SortAsync(SortKey.Extension, TestContext.Current.CancellationToken);
            OnUiThread("the beat after a sort", () => Beat(window, left));
            OnUiThread("select all", workspace.SelectAll);
            OnUiThread("the beat after select all", () => Beat(window, left));
            Assert.Equal(Count, left.List.MarkedCount);
            Assert.Equal($"{Count} / {Count} selected", left.StatusText);
            OnUiThread("invert", workspace.Invert);
            OnUiThread("the beat after invert", () => Beat(window, left));
            Assert.Equal(0, left.List.MarkedCount);
            OnUiThread("a filter", () => workspace.SetFilter(PanelSide.Left, "9999"));
            OnUiThread("the beat after a filter", () => Beat(window, left));
            Assert.Equal(20, left.List.Items.Count); // ".." and the 19 names holding 9999
        }
        finally
        {
            window.Close();
            Directory.Delete(dir, true);
        }
    }

    private static void OnUiThread(string step, Action work)
    {
        var clock = Stopwatch.StartNew();
        work();
        Assert.True(clock.Elapsed < Budget, $"{step} held the UI thread {clock.ElapsedMilliseconds} ms");
    }

    private static void Beat(Window window, PanelPaneModel pane)
    {
        pane.Tick();
        Settle(window);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Window Host(PanelPaneModel left, PanelPaneModel right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var rightView = right.CreateContent();
        Grid.SetColumn(rightView, 1);
        grid.Children.Add(left.CreateContent());
        grid.Children.Add(rightView);
        return new Window { Width = 1000, Height = 600, Content = grid };
    }

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
