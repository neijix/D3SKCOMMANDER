using Avalonia.Headless.XUnit;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.App.Tests.Controls.FileLists;

/// <summary>Home draws a line between the drives and the favorites; an ungrouped folder draws none.</summary>
public class FileListGroupLineTests
{
    [AvaloniaFact]
    public void HomeDrawsALineAboveTheFirstFavoriteOnly()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-groupline-").FullName;
        try
        {
            var a = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
            var b = Directory.CreateDirectory(Path.Combine(root, "b")).FullName;
            var fav1 = Directory.CreateDirectory(Path.Combine(root, "fav1")).FullName;
            var fav2 = Directory.CreateDirectory(Path.Combine(root, "fav2")).FullName;
            var text = Localizer.Load(Language.En, Language.En);
            var provider = new ComputerFileSystemProvider(new Sources([a, b], [fav1, fav2]), text, windows: false);
            var entries = provider.ListAsync(ComputerAddress.Text, false, default).ToBlockingEnumerable().Select(r => r.Value!).ToList();

            FileListHarness.Use(entries, h =>
            {
                Assert.Equal(4, h.List.Items.Count);
                Assert.Equal([false, false, true, false], Enumerable.Range(0, 4).Select(h.Control.RowHasGroupLine));
            }, localizer: text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void AFolderWithoutGroupsDrawsNoLine()
    {
        var entries = new[] { FileListSamples.Parent("/t"), FileListSamples.Folder("docs", "/t/docs"), FileListSamples.File("a.txt", "/t/a.txt", 5, "rw-r--r--") };
        FileListHarness.Use(entries, h => Assert.DoesNotContain(true, Enumerable.Range(0, 3).Select(h.Control.RowHasGroupLine)));
    }

    private sealed class Sources(IReadOnlyList<string> drives, IReadOnlyList<string> favorites) : IComputerSources
    {
        public IReadOnlyList<DriveEntry> MountedDrives() => [.. drives.Select(d => new DriveEntry(d, Path.GetFileName(d), "ext4", 1, 2))];

        public IReadOnlyList<ConnectedShare> ConnectedShares() => [];

        public IReadOnlyList<UserPlace> Places() => [];

        public IReadOnlyList<HotlistItem> Favorites() => [.. favorites.Select(f => new HotlistItem(Path.GetFileName(f), f))];

        public IReadOnlyList<string> MountPoints() => [];
    }
}
