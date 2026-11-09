using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace D3skCommander.Core.Tests.Settings;

/// <summary>AC3: favorites are hotlist items that may name a file; the JSON gains no field.</summary>
public class HotlistTests
{
    private static readonly Func<string, bool> No = _ => false;

    [Fact]
    public void AnOldFoldersOnlyFileLoadsUnchanged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "d3sk-hotlist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(
                Path.Combine(folder, SettingsFileNames.Hotlist),
                """{"schema":1,"items":[{"name":"Docs","path":"/home/docs"},{"name":"Work","path":"/var/work"}]}""");
            using var store = new HotlistStore(folder, new FakeTimeProvider());
            Assert.Equal(["Docs", "Work"], store.Current.Items.Select(i => i.Name));
            Assert.Equal(["/home/docs", "/var/work"], store.Current.Items.Select(i => i.Path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AFileEntryRoundTripsWithNoNewField()
    {
        var folder = Path.Combine(Path.GetTempPath(), "d3sk-hotlist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var clock = new FakeTimeProvider();
            clock.SetUtcNow(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));
            using (var store = new HotlistStore(folder, clock))
            {
                store.Save(new Hotlist { Items = [new HotlistItem("Report", "/home/docs/report.pdf")] });
                clock.Advance(TimeSpan.FromMilliseconds(SettingsStore<Hotlist>.DebounceMilliseconds));
            }

            var text = File.ReadAllText(Path.Combine(folder, SettingsFileNames.Hotlist));
            using var json = System.Text.Json.JsonDocument.Parse(text);
            var item = json.RootElement.GetProperty("items")[0];
            Assert.Equal(["name", "path"], item.EnumerateObject().Select(p => p.Name));
            using var loaded = new HotlistStore(folder, clock);
            Assert.Equal("/home/docs/report.pdf", loaded.Current.Items[0].Path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AFolderResolvesToGoToThatFolder()
    {
        var target = HotlistTarget.Resolve(new HotlistItem("Docs", "/home/docs"), p => p == "/home/docs", No);

        Assert.Equal(HotlistTargetKind.Folder, target.Kind);
        Assert.Equal("/home/docs", target.Folder);
        Assert.Null(target.CursorName);
    }

    [Fact]
    public void AFileResolvesToItsFolderWithTheCursorOnIt()
    {
        var path = Path.Combine("home", "docs", "report.pdf");
        var target = HotlistTarget.Resolve(new HotlistItem("Report", path), No, p => p == path);

        Assert.Equal(HotlistTargetKind.File, target.Kind);
        Assert.Equal(Path.Combine("home", "docs"), target.Folder);
        Assert.Equal("report.pdf", target.CursorName);
        Assert.Equal(path, target.Path);
    }

    [Fact]
    public void AMissingTargetCarriesItsPath()
    {
        var target = HotlistTarget.Resolve(new HotlistItem("Gone", "/gone/x.txt"), No, No);

        Assert.Equal(HotlistTargetKind.Missing, target.Kind);
        Assert.Equal("/gone/x.txt", target.Path);
        Assert.Null(target.CursorName);
    }

    [Fact]
    public void AddingAPathAlreadyListedReplacesIt()
    {
        var hotlist = new Hotlist { Items = [new HotlistItem("Old", "/a"), new HotlistItem("B", "/b")] };

        hotlist.Add(new HotlistItem("New", "/a"));

        Assert.Equal(["B", "New"], hotlist.Items.Select(i => i.Name));
    }
}
