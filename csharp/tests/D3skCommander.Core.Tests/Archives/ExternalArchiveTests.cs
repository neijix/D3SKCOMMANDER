using D3skCommander.Core.Archives;
using D3skCommander.Core.Common;
using Xunit;

namespace D3skCommander.Core.Tests.Archives;

/// <summary>P14: the 6 external tests of <c>src/core/archive.rs</c>.</summary>
public class ExternalArchiveTests
{
    private const string MissingTool =
        "this archive format needs an external tool: install 7-Zip (7z) or unrar and make sure it is on PATH";

    [Fact]
    public void UnknownAndExternalKinds()
    {
        using var bench = new Bench();
        var txt = bench.PathOf("a.txt");
        File.WriteAllText(txt, "x");
        var listed = ArchiveIo.List(txt);
        Assert.False(listed.IsSuccess);
        Assert.Equal(ErrorKind.Unsupported, listed.Error!.Kind);

        var created = Arc.Create(bench.PathOf("a.rar"), ArchiveKind.External, new[] { txt }, 1);
        Assert.False(created.IsSuccess);
        Assert.Equal(ErrorKind.Unsupported, created.Error!.Kind);
    }

    [Fact]
    public void ExternalSltOutputIsParsed()
    {
        var readme = ReadFixture("7z", "26.00", "README.txt");
        Assert.Contains("7z l -slt", readme, StringComparison.Ordinal);
        Assert.Empty(ArchiveExternal.ParseSlt(readme));

        var rar = ArchiveExternal.ParseSlt(ReadFixture("7z", "26.00", "rar-slt.txt"));
        Assert.Equal(5, rar.Count);
        var test = Find(rar, "test.txt");
        Assert.False(test.IsDirectory);
        Assert.Equal(20, test.Size);
        Assert.Equal(20, test.CompressedSize);
        AssertClock(test, 2011, 6, 26, 14, 53, 46);
        Assert.False(Find(rar, "testlink").IsDirectory);
        Assert.Equal(8, Find(rar, "testlink").Size);
        Assert.False(Find(rar, "testdir/test.txt").IsDirectory);
        Assert.Equal(20, Find(rar, "testdir/test.txt").Size);
        Assert.True(Find(rar, "testdir").IsDirectory);
        Assert.True(Find(rar, "testemptydir").IsDirectory);
        foreach (var entry in rar)
        {
            Assert.DoesNotContain('\\', entry.Path);
        }

        AssertNotes(ArchiveExternal.ParseSlt(ReadFixture("7z", "26.00", "seven-zip-slt.txt")));
        AssertNotes(ArchiveExternal.ParseSlt(ReadFixture("7z", "26.00", "zip-slt.txt")));

        List<string>? seen = null;
        var listed = ArchiveExternal.List("sample.rar", "/usr/bin/7z", (_, args) =>
        {
            seen = new List<string>(args);
            return ExternalToolOutput.Ok(ReadFixture("7z", "26.00", "rar-slt.txt"));
        });
        Assert.Equal(new[] { "l", "-slt", "--", "sample.rar" }, seen);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        Assert.Equal(rar.Count, listed.Value.Count);
        Assert.Equal(20, Find(listed.Value, "test.txt").Size);
    }

    [Fact]
    public void ExternalLbOutputIsParsed()
    {
        var readme = ReadFixture("unrar", "7.20", "README.txt");
        Assert.Contains("unrar lb", readme, StringComparison.Ordinal);
        var text = ReadFixture("unrar", "7.20", "lb.txt");
        AssertLb(ArchiveExternal.ParseLb(text));
        AssertLb(ArchiveExternal.ParseLb(text.Replace("\n", "\r\n", StringComparison.Ordinal)));

        List<string>? seen = null;
        var listed = ArchiveExternal.List("sample.rar", "/usr/bin/unrar", (_, args) =>
        {
            seen = new List<string>(args);
            return ExternalToolOutput.Ok(text);
        });
        Assert.Equal(new[] { "lb", "-p-", "--", "sample.rar" }, seen);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        AssertLb(listed.Value);
    }

    [Fact]
    public void ExternalExtractionRefusesUnsafeMemberNames()
    {
        foreach (var bad in new[] { "../evil", "dir/../../evil", "C:\\x", "dir\\C:x" })
        {
            var error = ArchiveExternal.UnsafeMembers(Members("ok.txt", bad), null);
            Assert.NotNull(error);
            Assert.Equal(ErrorKind.InvalidData, error.Kind);
            Assert.Contains("unsafe path", error.Message, StringComparison.Ordinal);
            Assert.Contains(ArchivePaths.Normalize(bad), error.Message, StringComparison.Ordinal);
        }

        Assert.NotNull(ArchiveExternal.UnsafeMembers(Members("dir/../../evil"), new[] { "dir" }));
        Assert.Null(ArchiveExternal.UnsafeMembers(Members("other/../../evil"), new[] { "dir" }));
        Assert.Null(ArchiveExternal.UnsafeMembers(Members("dir/a b.txt"), null));

        using var bench = new Bench();
        var archive = bench.PathOf("bad.rar");
        File.WriteAllText(archive, "Rar!");
        var extracted = false;
        var result = ArchiveExternal.Extract(
            archive,
            null,
            bench.PathOf("out"),
            null,
            TestContext.Current.CancellationToken,
            "/usr/bin/7z",
            (_, args) =>
            {
                if (args.Count > 0 && args[0] == "x")
                {
                    extracted = true;
                }

                return ExternalToolOutput.Ok(Slt("../evil"));
            });
        Assert.False(extracted);
        Assert.Equal(ErrorKind.InvalidData, result.Error!.Kind);
        Assert.Contains("unsafe path", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("parent directory component", result.Error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(bench.PathOf("out")));
    }

    [Fact]
    public void ExternalToolRoundTripWhenInstalled()
    {
        var tool = ArchiveExternal.FindTool();
        if (tool is null)
        {
            Assert.Skip("skipped: no 7z/unrar on PATH");
        }

        var stem = Path.GetFileNameWithoutExtension(tool);
        if (stem.StartsWith("unrar", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("skipped: unrar cannot read zip");
        }

        using var bench = new Bench();
        var archive = bench.Built(ArchiveKind.Zip, 6);
        var listed = ArchiveExternal.List(archive.Path, tool, ArchiveExternal.Run);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        Assert.Equal(3, bench.Find(listed.Value, "proj/top.txt").Size);

        var outDir = bench.PathOf("ext");
        var extracted = ArchiveExternal.Extract(
            archive.Path,
            new[] { "proj/docs" },
            outDir,
            null,
            TestContext.Current.CancellationToken,
            tool,
            ArchiveExternal.Run);
        Assert.True(extracted.IsSuccess, extracted.Error?.Message);
        Assert.Equal(2, extracted.Value);
        Assert.Equal("hello readme", File.ReadAllText(Path.Combine(outDir, "docs", "readme.txt")));
        Assert.False(Directory.Exists(Path.Combine(outDir, "proj")));
        foreach (var entry in Directory.GetFileSystemEntries(outDir))
        {
            Assert.DoesNotContain(".d3skcmd-extract-", Path.GetFileName(entry), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MissingExternalToolIsReportedAsUnsupported()
    {
        Assert.Null(ArchiveExternal.FindOnPath(null));
        Assert.Null(ArchiveExternal.FindOnPath(""));

        using var bench = new Bench();
        var rar = bench.PathOf("a.rar");
        File.WriteAllText(rar, "Rar!");
        var calls = 0;
        ExternalToolOutput Boom(string program, IReadOnlyList<string> arguments)
        {
            calls++;
            return ExternalToolOutput.FailedToStart(program + arguments.Count);
        }

        var listed = ArchiveExternal.List(rar, null, Boom);
        Assert.Equal(ErrorKind.Unsupported, listed.Error!.Kind);
        Assert.Equal(MissingTool, listed.Error.Message);
        Assert.Contains("7z", listed.Error.Message, StringComparison.Ordinal);

        var extracted = ArchiveExternal.Extract(
            rar,
            null,
            bench.PathOf("o"),
            null,
            TestContext.Current.CancellationToken,
            null,
            Boom);
        Assert.Equal(ErrorKind.Unsupported, extracted.Error!.Kind);
        Assert.Equal(MissingTool, extracted.Error.Message);
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(bench.PathOf("o")));

        if (ArchiveExternal.FindTool() is null)
        {
            var viaList = ArchiveIo.List(rar);
            Assert.Equal(ErrorKind.Unsupported, viaList.Error!.Kind);
            Assert.Contains("7z", viaList.Error.Message, StringComparison.Ordinal);
            var viaExtract = Arc.Extract(rar, null, bench.PathOf("via"));
            Assert.Equal(ErrorKind.Unsupported, viaExtract.Error!.Kind);
        }
    }

    private static void AssertNotes(IReadOnlyList<ArchiveEntry> entries)
    {
        var notes = Assert.Single(entries);
        Assert.Equal("notes.txt", notes.Path);
        Assert.False(notes.IsDirectory);
        Assert.Equal(6, notes.Size);
        Assert.Equal(6, notes.CompressedSize);
        AssertClock(notes, 2026, 11, 4, 19, 38, 15);
    }

    private static void AssertLb(IReadOnlyList<ArchiveEntry> entries)
    {
        Assert.Equal(5, entries.Count);
        Assert.False(Find(entries, "test.txt").IsDirectory);
        Assert.False(Find(entries, "testlink").IsDirectory);
        Assert.False(Find(entries, "testdir/test.txt").IsDirectory);
        Assert.True(Find(entries, "testdir").IsDirectory);
        Assert.False(Find(entries, "testemptydir").IsDirectory);
        Assert.Equal(0, Find(entries, "test.txt").Size);
        Assert.Null(Find(entries, "test.txt").CompressedSize);
        Assert.Null(Find(entries, "test.txt").Modified);
    }

    private static void AssertClock(ArchiveEntry entry, int year, int month, int day, int hour, int minute, int second)
    {
        Assert.NotNull(entry.Modified);
        var time = entry.Modified.Value.DateTime;
        Assert.Equal(year, time.Year);
        Assert.Equal(month, time.Month);
        Assert.Equal(day, time.Day);
        Assert.Equal(hour, time.Hour);
        Assert.Equal(minute, time.Minute);
        Assert.Equal(second, time.Second);
    }

    private static IReadOnlyList<ArchiveEntry> Members(params string[] paths)
    {
        var entries = new List<ArchiveEntry>(paths.Length);
        foreach (var path in paths)
        {
            entries.Add(new ArchiveEntry(ArchivePaths.Normalize(path), 1, null, false, null));
        }

        return entries;
    }

    private static string Slt(string path) =>
        "x\n----------\nPath = ok.txt\n\nPath = " + path + "\nSize = 1\n";

    private static ArchiveEntry Find(IReadOnlyList<ArchiveEntry> entries, string path)
    {
        foreach (var entry in entries)
        {
            if (entry.Path == path)
            {
                return entry;
            }
        }

        throw new InvalidOperationException(path + " not listed");
    }

    private static string ReadFixture(string folder, string version, string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", folder, version, name);
        Assert.True(File.Exists(path), path);
        return File.ReadAllText(path);
    }
}
