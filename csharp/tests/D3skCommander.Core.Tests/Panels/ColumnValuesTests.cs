using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class ColumnValuesTests
{
    private static readonly ILocalizer En = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
    private static readonly ILocalizer Fr = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);

    [Fact]
    public void CreatedAndAccessedShowTheFileTimesInTheListFormat()
    {
        using var folder = new Scratch();
        var path = folder.File("a.txt");
        var created = new DateTime(2021, 3, 4, 5, 6, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(path, created);
        File.SetLastAccessTimeUtc(path, created.AddDays(2));
        var entry = LocalFileSystem.ReadEntry(path).Value;

        Assert.Equal(LocalFileSystem.FormatTime(new DateTimeOffset(created)), ColumnValues.Created(entry));
        Assert.Equal(LocalFileSystem.FormatTime(new DateTimeOffset(created.AddDays(2))), ColumnValues.Accessed(entry));
    }

    [Fact]
    public void AMissingFileAndTheParentRowGiveBlankTimes()
    {
        var gone = new Entry("x", "/nope/never/x", false, false, false, 0, null, false, false, "");
        var parent = new Entry("..", "/tmp", true, false, true, 0, null, false, false, "");

        Assert.Equal("", ColumnValues.Created(gone));
        Assert.Equal("", ColumnValues.Accessed(gone));
        Assert.Equal("", ColumnValues.Created(parent));
        Assert.Equal("", ColumnValues.Owner(parent));
        Assert.Equal("", ColumnValues.Group(parent));
    }

    [Fact]
    public void OwnerAndGroupAreNamesOnUnixAndBlankOnWindows()
    {
        using var folder = new Scratch();
        var entry = LocalFileSystem.ReadEntry(folder.File("o.txt")).Value;

        var owner = ColumnValues.Owner(entry);
        var group = ColumnValues.Group(entry);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("", owner);
            Assert.Equal("", group);
            return;
        }

        Assert.NotEqual("", owner);
        Assert.NotEqual("", group);
        Assert.Equal(owner, ColumnValues.Owner(entry));
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(Environment.UserName, owner);
        }
    }

    [Theory]
    [InlineData("pic.png", "PNG image", "Image PNG")]
    [InlineData("notes.txt", "Text", "Texte")]
    [InlineData("pack.zip", "ZIP archive", "Archive ZIP")]
    [InlineData("Program.cs", "CS source", "Source CS")]
    [InlineData("README", "File", "Fichier")]
    [InlineData("data.xyz", "XYZ file", "Fichier XYZ")]
    public void KindIsAShortTypeTextInBothLanguages(string name, string english, string french)
    {
        var entry = new Entry(name, "/x/" + name, false, false, false, 1, null, false, false, "");

        Assert.Equal(english, ColumnValues.Kind(entry, En));
        Assert.Equal(french, ColumnValues.Kind(entry, Fr));
    }

    [Fact]
    public void KindOfAFolderALinkAndTheParentRow()
    {
        var folder = new Entry("d", "/x/d", true, false, false, 0, null, false, false, "");
        var link = new Entry("l", "/x/l", false, true, false, 0, null, false, false, "");
        var parent = new Entry("..", "/x", true, false, true, 0, null, false, false, "");

        Assert.Equal("Folder", ColumnValues.Kind(folder, En));
        Assert.Equal("Dossier", ColumnValues.Kind(folder, Fr));
        Assert.Equal("Link", ColumnValues.Kind(link, En));
        Assert.Equal("", ColumnValues.Kind(parent, En));
    }

    [Fact]
    public void SortingByKindGroupsByKindThenExtensionThenName()
    {
        var entries = new List<Entry>
        {
            new("b.txt", "/x/b.txt", false, false, false, 1, null, false, false, ""),
            new("a.png", "/x/a.png", false, false, false, 1, null, false, false, ""),
            new("c.jpg", "/x/c.jpg", false, false, false, 1, null, false, false, ""),
            new("a.txt", "/x/a.txt", false, false, false, 1, null, false, false, ""),
            new("zdir", "/x/zdir", true, false, false, 0, null, false, false, ""),
        };

        LocalFileSystem.Sort(entries, new SortOrder(SortKey.Kind, false));

        Assert.Equal(["zdir", "c.jpg", "a.png", "a.txt", "b.txt"], entries.Select(entry => entry.Name));
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "d3sk-colv-" + Guid.NewGuid().ToString("N"));

        public Scratch() => Directory.CreateDirectory(_path);

        public string File(string name)
        {
            var path = Path.Combine(_path, name);
            System.IO.File.WriteAllText(path, "x");
            return path;
        }

        public void Dispose() => Directory.Delete(_path, true);
    }
}
