using System.Globalization;
using System.Text.Json;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.Catalog;

/// <summary>P19 in the <c>.d3cat</c> JSON Lines format. The five v0.1 <c>catalog.rs</c> tests, renamed where the format changed.</summary>
public class CatalogTests
{
    [Fact]
    public async Task WriteThenReadRoundTrip()
    {
        using var dir = new TempFolder();
        var root = Path.Combine(dir.Path, "root");
        Directory.CreateDirectory(root);
        Tree(root);
        var output = Path.Combine(dir.Path, "cat.d3cat");

        var jobs = new JobService();
        var job = DiskCatalog.Start(jobs, "Disk catalog", root, output);
        var result = await job.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        var document = ReadOk(output);
        var header = document.Header;
        Assert.NotNull(header);
        Assert.Equal(DiskCatalog.SchemaVersion, header.Schema);
        Assert.Equal(Full(root), header.Root);
        Assert.Equal(5, document.Entries.Count);
        Assert.True(Find(document, "docs").IsDir);
        Assert.True(Find(document, "docs/deep").IsDir);
        Assert.True(Find(document, "empty").IsDir);
        Assert.False(Find(document, "a.txt").IsDir);
        Assert.Equal(5, Find(document, "a.txt").Size);
        Assert.Equal(2, Find(document, "docs/b.md").Size);
        Assert.NotNull(Find(document, "a.txt").Mtime);
    }

    [Fact]
    public void HeaderAndEntriesRoundTrip()
    {
        using var dir = new TempFolder();
        var root = Path.Combine(dir.Path, "r");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "d"));
        var file = Path.Combine(root, "d", "f");
        File.WriteAllText(file, "abc");
        var output = Path.Combine(dir.Path, "c.d3cat");
        var created = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        var written = DiskCatalog.Write(root, output, CancellationToken.None, created: created);
        Assert.True(written.IsSuccess, written.Error?.Message);
        Assert.Equal(2, written.Count);

        var lines = File.ReadAllLines(output);
        Assert.Equal(3, lines.Length);
        using (var header = JsonDocument.Parse(lines[0]))
        {
            Assert.Equal(1, header.RootElement.GetProperty("schema").GetInt32());
            Assert.Equal(Full(root), header.RootElement.GetProperty("root").GetString());
            Assert.Equal(created.ToString("O", CultureInfo.InvariantCulture), header.RootElement.GetProperty("created").GetString());
        }

        using (var directory = JsonDocument.Parse(lines[1]))
        {
            Assert.Equal("d", directory.RootElement.GetProperty("path").GetString());
            Assert.Equal(0, directory.RootElement.GetProperty("size").GetInt64());
            Assert.True(directory.RootElement.GetProperty("isDir").GetBoolean());
        }

        using (var entry = JsonDocument.Parse(lines[2]))
        {
            Assert.Equal("d/f", entry.RootElement.GetProperty("path").GetString());
            Assert.Equal(3, entry.RootElement.GetProperty("size").GetInt64());
            Assert.False(entry.RootElement.GetProperty("isDir").GetBoolean());
        }

        var document = ReadOk(output);
        Assert.Equal(created, document.Header?.Created);
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(file)), Find(document, "d/f").Mtime);
    }

    [Fact]
    public void CatalogInsideRootIsNotListed()
    {
        using var dir = new TempFolder();
        Tree(dir.Path);
        var output = Path.Combine(dir.Path, "self.d3cat");

        var written = DiskCatalog.Write(dir.Path, output, CancellationToken.None);
        Assert.True(written.IsSuccess, written.Error?.Message);
        Assert.Equal(5, written.Count);
        var document = ReadOk(output);
        Assert.DoesNotContain(document.Entries, entry => entry.Path == "self.d3cat");
    }

    [Fact]
    public void CancelRemovesOutput()
    {
        using var dir = new TempFolder();
        var root = Path.Combine(dir.Path, "r");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a"), "x");
        var output = Path.Combine(dir.Path, "c.d3cat");

        var written = DiskCatalog.Write(root, output, new CancellationToken(true));
        Assert.Equal(ErrorKind.Cancelled, written.Error?.Kind);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void ReadToleratesTruncatedLastLineAndEmptyFile()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "c.d3cat");
        var header = "{\"schema\":1,\"root\":\"/root\",\"created\":\"2026-11-04T00:00:00.0000000+00:00\"}";
        var directory = "{\"path\":\"x\",\"size\":0,\"mtime\":null,\"isDir\":true}";
        var file = "{\"path\":\"file\",\"size\":7,\"mtime\":null,\"isDir\":false}";
        File.WriteAllText(path, header + "\n" + directory + "\n" + file + "\n" + "{\"path\":\"cut\",\"size\":");

        var document = ReadOk(path);
        Assert.Equal("/root", document.Header?.Root);
        Assert.Equal(2, document.Entries.Count);
        Assert.Equal("x", document.Entries[0].Path);
        Assert.Equal(0, document.Entries[0].Size);
        Assert.True(document.Entries[0].IsDir);
        Assert.Null(document.Entries[0].Mtime);
        Assert.Equal(7, document.Entries[1].Size);

        File.WriteAllText(path, "");
        var empty = ReadOk(path);
        Assert.Null(empty.Header);
        Assert.Empty(empty.Entries);
    }

    [Fact]
    public void SearchWorksOffline()
    {
        using var dir = new TempFolder();
        var root = Path.Combine(dir.Path, "root");
        Directory.CreateDirectory(root);
        Tree(root);
        var output = Path.Combine(dir.Path, "cat.d3cat");
        var written = DiskCatalog.Write(root, output, CancellationToken.None);
        Assert.True(written.IsSuccess, written.Error?.Message);
        Directory.Delete(root, recursive: true);

        var document = ReadOk(output);
        var hit = Assert.Single(DiskCatalog.Search(document, "B.MD"));
        Assert.Equal("docs/b.md", hit.Path);
        Assert.Equal(2, hit.Size);
        Assert.Equal(5, DiskCatalog.Search(document, "  ").Count);
        Assert.Empty(DiskCatalog.Search(document, "missing"));
    }

    [Fact]
    public void DetectsACatalogByExtensionAndHeader()
    {
        using var dir = new TempFolder();
        var root = Path.Combine(dir.Path, "root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
        var output = Path.Combine(dir.Path, "disk.d3cat");
        var written = DiskCatalog.Write(root, output, CancellationToken.None);
        Assert.True(written.IsSuccess, written.Error?.Message);
        Assert.True(DiskCatalog.IsCatalog(output));

        var plain = Path.Combine(dir.Path, "notes.d3cat");
        File.WriteAllText(plain, "just a cat story\nwithout a header\n");
        Assert.False(DiskCatalog.IsCatalog(plain));
        Assert.Equal(ErrorKind.InvalidData, DiskCatalog.Read(plain).Error?.Kind);

        var other = Path.Combine(dir.Path, "copy.txt");
        File.Copy(output, other);
        Assert.False(DiskCatalog.IsCatalog(other));
    }

    private static void Tree(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "docs", "deep"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(root, "docs", "b.md"), "12");
    }

    private static CatalogDocument ReadOk(string path)
    {
        var read = DiskCatalog.Read(path);
        Assert.True(read.IsSuccess, read.Error?.Message);
        return read.Value;
    }

    private static CatalogEntry Find(CatalogDocument document, string path)
    {
        foreach (var entry in document.Entries)
        {
            if (entry.Path == path)
            {
                return entry;
            }
        }

        throw new InvalidOperationException("missing " + path);
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("d3sk-cat-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
