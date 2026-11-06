using System.Text.Json;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class ColumnSetTests
{
    [Fact]
    public void WithoutStoredSetsThereAreDefaultAndDetails()
    {
        var sets = new Preferences().AllColumnSets();

        Assert.Equal([ColumnSetCatalog.DefaultName, ColumnSetCatalog.DetailsName], sets.Select(set => set.Name));
        Assert.Equal(
            [ColumnId.Extension, ColumnId.Size, ColumnId.Modified, ColumnId.Attributes],
            sets[0].Columns.Select(column => column.Id));
        Assert.Equal(ColumnInfo.Optional, sets[1].Columns.Select(column => column.Id));
        Assert.All(sets.SelectMany(set => set.Columns), column => Assert.Equal(0, column.Width));
    }

    [Theory]
    [InlineData(true, false, false, false, "Extension")]
    [InlineData(false, true, false, false, "Size")]
    [InlineData(false, false, true, false, "Modified")]
    [InlineData(false, false, false, true, "Attributes")]
    [InlineData(false, false, false, false, "")]
    public void DefaultIsMigratedFromTheFourOldFlags(bool extension, bool size, bool date, bool attributes, string expected)
    {
        var preferences = new Preferences { Columns = new Columns(extension, size, date, attributes) };

        var migrated = preferences.AllColumnSets()[0];

        Assert.Equal(ColumnSetCatalog.DefaultName, migrated.Name);
        Assert.Equal(expected, string.Concat(migrated.Columns.Select(column => column.Id)));
    }

    [Fact]
    public void AStoredDefaultWinsOverTheOldFlags()
    {
        var preferences = new Preferences
        {
            Columns = new Columns(true, true, true, true),
            ColumnSets = [new ColumnSet("default", [new ColumnSetColumn(ColumnId.Kind, 90)])],
        };

        var sets = preferences.AllColumnSets();

        Assert.Equal(ColumnSetCatalog.DefaultName, sets[0].Name);
        var only = Assert.Single(sets[0].Columns);
        Assert.Equal(ColumnId.Kind, only.Id);
        Assert.Equal(90, only.Width);
        Assert.Equal(ColumnSetCatalog.DetailsName, sets[1].Name);
    }

    [Fact]
    public void ColumnSetsSurviveAJsonRoundTripInPreferences()
    {
        using var folder = new TempFolder();
        var preferences = new Preferences
        {
            ColumnSets =
            [
                new ColumnSet("Photos", [new ColumnSetColumn(ColumnId.Size, 80), new ColumnSetColumn(ColumnId.Created), new ColumnSetColumn(ColumnId.Owner, 120)]),
            ],
        };
        using (var store = new PreferencesStore(folder.Path))
        {
            store.Save(preferences);
            store.Flush();
        }

        using var loaded = new PreferencesStore(folder.Path);

        var photos = Assert.Single(loaded.Current.ColumnSets);
        Assert.Equal("Photos", photos.Name);
        Assert.Equal([ColumnId.Size, ColumnId.Created, ColumnId.Owner], photos.Columns.Select(column => column.Id));
        Assert.Equal([80d, 0d, 120d], photos.Columns.Select(column => column.Width));
        Assert.Contains("\"owner\"", File.ReadAllText(System.IO.Path.Combine(folder.Path, D3skCommander.Core.SettingsLocation.SettingsFileNames.Preferences)).ToLowerInvariant());
    }

    [Fact]
    public void AnOldFileWithoutSetsLoadsAndMigrates()
    {
        using var folder = new TempFolder();
        File.WriteAllText(
            System.IO.Path.Combine(folder.Path, D3skCommander.Core.SettingsLocation.SettingsFileNames.Preferences),
            """{"schema":1,"columns":{"extension":false,"size":true,"date":false,"attributes":true}}""");

        using var loaded = new PreferencesStore(folder.Path);

        Assert.Empty(loaded.Current.ColumnSets);
        Assert.Equal(
            [ColumnId.Size, ColumnId.Attributes],
            loaded.Current.AllColumnSets()[0].Columns.Select(column => column.Id));
    }

    [Fact]
    public void UnknownNamesFallBackToDefaultAndNamesIgnoreCase()
    {
        var sets = new Preferences { ColumnSets = [new ColumnSet("Wide", [new ColumnSetColumn(ColumnId.Group)])] }.AllColumnSets();

        Assert.Equal(ColumnSetCatalog.DefaultName, ColumnSetCatalog.Resolve(sets, "gone").Name);
        Assert.Equal(ColumnSetCatalog.DefaultName, ColumnSetCatalog.Resolve(sets, null).Name);
        Assert.Equal("Wide", ColumnSetCatalog.Resolve(sets, "WIDE").Name);
    }

    [Fact]
    public void NextCyclesThroughTheSetsAndWraps()
    {
        var sets = new Preferences { ColumnSets = [new ColumnSet("Wide", [])] }.AllColumnSets();

        Assert.Equal("Details", ColumnSetCatalog.Next(sets, "Default").Name);
        Assert.Equal("Wide", ColumnSetCatalog.Next(sets, "Details").Name);
        Assert.Equal("Default", ColumnSetCatalog.Next(sets, "Wide").Name);
        Assert.Equal("Details", ColumnSetCatalog.Next(sets, "unknown").Name);
    }

    [Fact]
    public void NormalizingDropsTheNameColumnDuplicatesAndBadWidths()
    {
        var messy = new ColumnSet(
            "Messy",
            [
                new ColumnSetColumn(ColumnId.Name, 50),
                new ColumnSetColumn(ColumnId.Size, 5),
                new ColumnSetColumn(ColumnId.Size, 90),
                new ColumnSetColumn(ColumnId.Kind, -4),
            ]);

        var clean = messy.Normalized();

        Assert.Equal([ColumnId.Size, ColumnId.Kind], clean.Columns.Select(column => column.Id));
        Assert.Equal([ColumnSet.MinWidth, 0d], clean.Columns.Select(column => column.Width));
    }

    [Fact]
    public void ANarrowPaneDropsTheLeastUsefulColumnsFirst()
    {
        var details = ColumnSetCatalog.Details();

        Assert.Equal(details.Signature(), details.Fitting(5000f, 0f).Signature());
        var narrow = details.Fitting(ColumnSet.NameMinimum + ColumnInfo.FitWidth(ColumnId.Size) + 148f, 0f);
        Assert.Equal([ColumnId.Size, ColumnId.Modified], narrow.Columns.Select(column => column.Id));
        Assert.Empty(details.Fitting(10f, 0f).Columns);
        var noOwner = details.Fitting(ColumnSet.NameMinimum + new[] { ColumnId.Extension, ColumnId.Size, ColumnId.Modified, ColumnId.Created, ColumnId.Accessed, ColumnId.Attributes, ColumnId.Kind }.Sum(ColumnInfo.FitWidth), 0f);
        Assert.DoesNotContain(noOwner.Columns, column => column.Id is ColumnId.Owner or ColumnId.Group);
        Assert.Contains(noOwner.Columns, column => column.Id == ColumnId.Kind);
    }

    [Fact]
    public void UniqueNameAddsANumber()
    {
        Assert.Equal("New", ColumnSetCatalog.UniqueName(["Default"], "New"));
        Assert.Equal("New (2)", ColumnSetCatalog.UniqueName(["Default", "new"], "New"));
        Assert.Equal("New (3)", ColumnSetCatalog.UniqueName(["New", "New (2)"], "New"));
    }

    [Fact]
    public void BuiltInNamesAreRecognised()
    {
        Assert.True(ColumnSetCatalog.IsBuiltIn("default"));
        Assert.True(ColumnSetCatalog.IsBuiltIn("Details"));
        Assert.False(ColumnSetCatalog.IsBuiltIn("Photos"));
    }

    [Fact]
    public void OnlyTheColumnsThatAreCheapToCompareSort()
    {
        Assert.True(ColumnInfo.IsSortable(ColumnId.Kind));
        Assert.False(ColumnInfo.IsSortable(ColumnId.Created));
        Assert.False(ColumnInfo.IsSortable(ColumnId.Owner));
        Assert.Equal(ColumnId.Created, ColumnInfo.FromHeaderKey("col.created"));
        Assert.Null(ColumnInfo.FromHeaderKey("nope"));
    }

    [Fact]
    public void TheJsonOfASetUsesNamesNotNumbers()
    {
        var json = JsonSerializer.Serialize(
            new ColumnSetColumn(ColumnId.Accessed, 77),
            new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

        Assert.Contains("Accessed", json, StringComparison.Ordinal);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "d3sk-cols-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
