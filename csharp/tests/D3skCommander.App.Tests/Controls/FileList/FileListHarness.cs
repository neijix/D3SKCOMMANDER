using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Controls.FileLists;

internal static class FileListHarness
{
    public static void Use(
        IEnumerable<Entry> entries,
        Action<FileList> body,
        FileListView view = FileListView.Full,
        double height = 360,
        ILocalizer? localizer = null)
    {
        body(Open(entries, view, height, localizer));
    }

    public static FileList Open(
        IEnumerable<Entry> entries,
        FileListView view,
        double height,
        ILocalizer? localizer)
    {
        return new FileList(localizer ?? TextCatalog.Load(Language.En, Language.En))
        {
            View = view,
            Entries = entries,
            ViewportHeight = height,
        };
    }

    public static void Press(FileList list, int index, FileListModifiers modifiers, int frames)
    {
        list.PointerDown(index, modifiers);
        var steps = frames < 1 ? 1 : frames;
        for (var i = 0; i < steps; i++)
        {
            list.PointerMove(index, modifiers);
        }

        list.PointerUp(index, modifiers);
    }

    public static void Key(FileList list, FileListKey key) => list.Key(key);
}
