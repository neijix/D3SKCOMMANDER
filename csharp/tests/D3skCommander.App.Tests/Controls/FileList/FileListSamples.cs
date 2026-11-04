using System.Globalization;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Tests.Controls.FileLists;

internal static class FileListSamples
{
    public static readonly DateTimeOffset Stamp = new(2024, 6, 15, 13, 45, 0, TimeSpan.Zero);

    public static Entry File(
        string name,
        string path,
        ulong size,
        string attributes,
        bool hidden = false,
        bool readOnly = false,
        bool link = false) =>
        new(name, path, false, link, false, size, Stamp, hidden, readOnly, attributes);

    public static Entry Folder(string name, string path, ulong size = 0) =>
        new(name, path, true, false, false, size, Stamp, false, false, "rwxr-xr-x");

    public static Entry Parent(string path = "") =>
        new("..", path, true, false, true, 0, null, false, false, "");

    public static List<Entry> Files(int count)
    {
        var list = new List<Entry>(count);
        for (var i = 0; i < count; i++)
        {
            var name = "f" + i.ToString(CultureInfo.InvariantCulture) + ".txt";
            list.Add(File(name, "/t/" + name, (ulong)i, "rw-r--r--"));
        }

        return list;
    }
}
