using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Controls.FileLists;

internal static class FileListHarness
{
    public static void Use(
        IEnumerable<Entry> entries,
        Action<Window, FileList> body,
        FileListView view = FileListView.Full,
        double height = 360,
        ILocalizer? localizer = null)
    {
        var (window, list) = Open(entries, view, height, localizer);
        try
        {
            body(window, list);
        }
        finally
        {
            window.Close();
        }
    }

    public static (Window Window, FileList List) Open(
        IEnumerable<Entry> entries,
        FileListView view,
        double height,
        ILocalizer? localizer)
    {
        var list = new FileList(localizer ?? TextCatalog.Load(Language.En, Language.En))
        {
            View = view,
            Entries = entries,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var window = new Window { Width = 960, Height = height, Content = list };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        list.Focus();
        return (window, list);
    }

    public static Point RowCenter(FileList list, Window window, int index)
    {
        var box = list.FindControl<ListBox>("Rows");
        Assert.NotNull(box);
        var container = box.ContainerFromIndex(index) as Visual;
        Assert.NotNull(container);
        Assert.True(container.Bounds.Width > 0);
        Assert.True(container.Bounds.Height > 0);
        var center = new Point(container.Bounds.Width / 2, container.Bounds.Height / 2);
        var point = container.TranslatePoint(center, window);
        Assert.NotNull(point);
        return point.Value;
    }

    public static void Press(Window window, Point at, RawInputModifiers modifiers, double shift, int frames)
    {
        window.MouseDown(at, MouseButton.Left, modifiers);
        var moved = at + new Vector(shift, shift / 2);
        var steps = frames < 1 ? 1 : frames;
        for (var i = 0; i < steps; i++)
        {
            window.MouseMove(moved, modifiers);
        }

        window.MouseUp(moved, MouseButton.Left, modifiers);
    }

    public static void Key(Window window, Key key) =>
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, "");
}
