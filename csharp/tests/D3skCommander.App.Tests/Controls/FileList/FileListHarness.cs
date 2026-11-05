using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Controls.FileLists;

/// <summary>A file list control in a shown headless window, driven by real pointer and key events.</summary>
internal sealed class FileListHarness : IDisposable
{
    private readonly Window _window;

    private FileListHarness(Window window, FileListControl control)
    {
        _window = window;
        Control = control;
    }

    public FileListControl Control { get; }

    public FileList List => Control.List;

    public static void Use(
        IEnumerable<Entry> entries,
        Action<FileListHarness> body,
        FileListView view = FileListView.Full,
        double height = 360,
        ILocalizer? localizer = null)
    {
        using var harness = Open(entries, view, height, localizer);
        body(harness);
    }

    public static FileListHarness Open(
        IEnumerable<Entry> entries,
        FileListView view,
        double height,
        ILocalizer? localizer)
    {
        var control = new FileListControl(localizer ?? TextCatalog.Load(Language.En, Language.En));
        var window = new Window { Width = 600, Height = height, Content = control };
        window.Show();
        control.List.View = view;
        control.List.Entries = entries;
        var harness = new FileListHarness(window, control);
        control.Focus();
        harness.Settle();
        return harness;
    }

    public void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        _window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        _window.UpdateLayout();
    }

    public Point PointOf(int index) =>
        Control.RowPoint(index, _window) ?? throw new InvalidOperationException("row " + index + " is not realized");

    public void Press(int index, RawInputModifiers modifiers = RawInputModifiers.None, int moves = 0)
    {
        var at = PointOf(index);
        _window.MouseMove(at, modifiers);
        _window.MouseDown(at, MouseButton.Left, modifiers);
        for (var i = 0; i < moves; i++)
        {
            _window.MouseMove(new Point(at.X - 1 - (i % 3), at.Y), modifiers);
        }

        _window.MouseUp(at, MouseButton.Left, modifiers);
        Settle();
    }

    public void DoubleClick(int index)
    {
        var at = PointOf(index);
        _window.MouseDown(at, MouseButton.Left);
        _window.MouseUp(at, MouseButton.Left);
        _window.MouseDown(at, MouseButton.Left);
        _window.MouseUp(at, MouseButton.Left);
        Settle();
    }

    public void Wheel(int index, double delta)
    {
        _window.MouseWheel(PointOf(index), new Vector(0, delta));
        Settle();
    }

    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        _window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Settle();
    }

    public void Dispose() => _window.Close();
}
