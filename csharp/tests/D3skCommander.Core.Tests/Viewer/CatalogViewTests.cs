using D3skCommander.Core.Catalog;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Viewer;
using Xunit;

namespace D3skCommander.Core.Tests.Viewer;

/// <summary>The two v0.1 <c>src/ui/lister/table.rs</c> catalog tests, in the <c>.d3cat</c> format.</summary>
public class CatalogViewTests
{
    [Fact]
    public void CatalogIsDetectedAndFiltered()
    {
        using var dir = new ViewerTemp();
        var root = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "a.txt"), "hi");
        var catalog = Path.Combine(dir.Path, "disk.d3cat");
        var written = DiskCatalog.Write(root, catalog, CancellationToken.None);
        Assert.True(written.IsSuccess, written.Error?.Message);

        var table = ViewerTables.TryCatalog(catalog);
        Assert.NotNull(table);
        Assert.Equal(Full(root), table.Note);
        Assert.Equal(["Path", "Size", "Date"], table.Columns);
        Assert.Equal([60, 22, 19], table.Widths);
        Assert.Equal(2, table.Rows.Count);
        Assert.Contains(table.Rows, row => row[1] == "<DIR>");

        var hit = Assert.Single(table.Filtered("A.TXT"));
        Assert.Contains("a.txt", table.Rows[hit][0], StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("<DIR>", table.Rows[hit][1]);
        Assert.Equal(2, table.Filtered("").Count);
        AssertShownSizeAndTime(catalog, table, hit);

        var viewer = FileViewer.Open(catalog);
        Assert.Equal(ViewerMode.Table, viewer.Mode);
        Assert.Equal([ViewerMode.Table, ViewerMode.Text], viewer.Modes);
        viewer.SetFilter("A.TXT");
        Assert.Equal(table.Filtered("A.TXT"), viewer.FilteredRows);
    }

    [Fact]
    public void PlainTextNamedD3catIsNotACatalog()
    {
        using var dir = new ViewerTemp();
        var plain = Path.Combine(dir.Path, "meow.d3cat");
        File.WriteAllText(plain, "just a cat story\nwithout a header\n");
        Assert.Null(ViewerTables.TryCatalog(plain));
        var asText = FileViewer.Open(plain);
        Assert.Equal(ViewerMode.Text, asText.Mode);
        Assert.Null(asText.Table);

        var root = Path.Combine(dir.Path, "root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        var catalog = Path.Combine(dir.Path, "disk.d3cat");
        var written = DiskCatalog.Write(root, catalog, CancellationToken.None);
        Assert.True(written.IsSuccess, written.Error?.Message);
        var other = Path.Combine(dir.Path, "x.txt");
        File.Copy(catalog, other);
        Assert.Null(ViewerTables.TryCatalog(other));
    }

    private static void AssertShownSizeAndTime(string catalog, ViewerTable table, int hit)
    {
        var document = DiskCatalog.Read(catalog);
        Assert.True(document.IsSuccess, document.Error?.Message);
        CatalogEntry? file = null;
        foreach (var entry in document.Value.Entries)
        {
            if (!entry.IsDir && entry.Path.Contains("a.txt", StringComparison.Ordinal))
            {
                file = entry;
            }
        }

        Assert.NotNull(file);
        var bytes = (ulong)file.Size;
        var shown = LocalFileSystem.FormatGrouped(bytes) + " (" + LocalFileSystem.FormatSize(bytes) + ")";
        Assert.Equal(shown, table.Rows[hit][1]);
        Assert.Equal(LocalFileSystem.FormatTime(file.Mtime), table.Rows[hit][2]);
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
