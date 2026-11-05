using D3skCommander.Core.Archives;
using D3skCommander.Core.Common;
using Xunit;

namespace D3skCommander.Core.Tests.Archives;

/// <summary>P13: the 18 native tests of <c>src/core/archive.rs</c>.</summary>
public class ArchiveTests
{
    internal const string UnicodeName = "données-é-日本.txt";
    internal const long ReadmeTime = 1_600_000_000;

    [Fact]
    public void FromPathByExtension()
    {
        Assert.Equal(ArchiveKind.Zip, ArchiveKinds.FromPath("a.zip"));
        Assert.Equal(ArchiveKind.Zip, ArchiveKinds.FromPath("A.JAR"));
        Assert.Equal(ArchiveKind.Tar, ArchiveKinds.FromPath("a.tar"));
        Assert.Equal(ArchiveKind.TarGz, ArchiveKinds.FromPath("a.tar.gz"));
        Assert.Equal(ArchiveKind.TarGz, ArchiveKinds.FromPath("dir/A.TGZ"));
        Assert.Equal(ArchiveKind.SevenZ, ArchiveKinds.FromPath("a.7z"));
        foreach (var ext in new[] { "rar", "arj", "lzh", "lha", "cab", "ace", "uc2", "gz", "bz2", "xz", "iso" })
        {
            Assert.Equal(ArchiveKind.External, ArchiveKinds.FromPath("a." + ext));
        }

        Assert.Equal(ArchiveKind.External, ArchiveKinds.FromPath("a.tar.bz2"));
        Assert.Null(ArchiveKinds.FromPath("a.txt"));
        Assert.Null(ArchiveKinds.FromPath("zip"));
    }

    [Fact]
    public void KindMetadata()
    {
        Assert.Equal(
            new[] { ArchiveKind.Zip, ArchiveKind.Tar, ArchiveKind.TarGz, ArchiveKind.SevenZ },
            ArchiveKinds.Creatable());
        var extensions = new List<string>();
        foreach (var kind in ArchiveKinds.Creatable())
        {
            extensions.Add(ArchiveKinds.Extension(kind));
        }

        Assert.Equal(new[] { "zip", "tar", "tar.gz", "7z" }, extensions);
        Assert.False(string.IsNullOrEmpty(ArchiveKinds.Label(ArchiveKind.Zip)));
    }

    [Fact]
    public void RoundTripEveryCreatableKind()
    {
        foreach (var kind in ArchiveKinds.Creatable())
        {
            foreach (var level in new[] { 0, 6, 9 })
            {
                using var bench = new Bench();
                var archive = bench.Built(kind, level);
                var entries = ArchiveIo.List(archive.Path).Value;
                foreach (var dir in new[] { "proj", "proj/docs", "proj/docs/sub", "proj/empty" })
                {
                    Assert.True(bench.Find(entries, dir).IsDirectory, kind + " " + dir);
                }

                var uni = "proj/docs/sub/" + UnicodeName;
                var entry = bench.Find(entries, uni);
                Assert.False(entry.IsDirectory);
                Assert.Equal(EncodingLength("unicode body"), entry.Size);

                var extracted = bench.PathOf("extracted");
                var count = Arc.Extract(archive.Path, null, extracted).Value;
                Assert.Equal(3, count);
                var proj = Path.Combine(extracted, "proj");
                Assert.Equal("top", File.ReadAllText(Path.Combine(proj, "top.txt")));
                Assert.Equal("unicode body", File.ReadAllText(Path.Combine(proj, "docs", "sub", UnicodeName)));
                Assert.True(Directory.Exists(Path.Combine(proj, "empty")), kind.ToString());
                var secs = UnixSeconds(Path.Combine(proj, "docs", "readme.txt"));
                Assert.True(Math.Abs(secs - ReadmeTime) <= 2, kind + " mtime " + secs);
            }
        }
    }

    [Fact]
    public void ListedModifiedTimeIsKept()
    {
        foreach (var kind in ArchiveKinds.Creatable())
        {
            using var bench = new Bench();
            var archive = bench.Built(kind, 6);
            var entries = ArchiveIo.List(archive.Path).Value;
            var modified = bench.Find(entries, "proj/docs/readme.txt").Modified;
            Assert.NotNull(modified);
            var secs = modified.Value.ToUnixTimeSeconds();
            Assert.True(Math.Abs(secs - ReadmeTime) <= 2, kind + " " + secs);
        }
    }

    [Fact]
    public void ExtractASelectedSubfolder()
    {
        foreach (var kind in ArchiveKinds.Creatable())
        {
            using var bench = new Bench();
            var archive = bench.Built(kind, 6);
            var outDir = bench.PathOf("sel");
            var count = Arc.Extract(archive.Path, new[] { "proj/docs" }, outDir).Value;
            Assert.Equal(2, count);
            Assert.True(File.Exists(Path.Combine(outDir, "docs", "readme.txt")), kind.ToString());
            Assert.True(File.Exists(Path.Combine(outDir, "docs", "sub", UnicodeName)));
            Assert.False(File.Exists(Path.Combine(outDir, "top.txt")));
            Assert.False(Directory.Exists(Path.Combine(outDir, "proj")));
        }
    }

    [Fact]
    public void ExtractSelectionKeepsPathsBelowTheCommonParent()
    {
        using var bench = new Bench();
        var archive = bench.Built(ArchiveKind.Zip, 6);

        var one = bench.PathOf("one");
        Arc.Extract(archive.Path, new[] { "proj/docs/readme.txt" }, one);
        Assert.True(File.Exists(Path.Combine(one, "readme.txt")));

        var two = bench.PathOf("two");
        Arc.Extract(archive.Path, new[] { "proj/top.txt", "proj/docs/readme.txt" }, two);
        Assert.True(File.Exists(Path.Combine(two, "top.txt")));
        Assert.True(File.Exists(Path.Combine(two, "docs", "readme.txt")));

        var none = bench.PathOf("none");
        Assert.Equal(0, Arc.Extract(archive.Path, Array.Empty<string>(), none).Value);
    }

    [Fact]
    public void SelectionDoesNotMatchNamePrefixes()
    {
        using var bench = new Bench();
        var proj = bench.PathOf("p");
        Directory.CreateDirectory(Path.Combine(proj, "doc"));
        Directory.CreateDirectory(Path.Combine(proj, "docs"));
        File.WriteAllText(Path.Combine(proj, "doc", "a.txt"), "a");
        File.WriteAllText(Path.Combine(proj, "docs", "b.txt"), "b");
        var archive = bench.PathOf("p.tar");
        var created = Arc.Create(archive, ArchiveKind.Tar, new[] { proj }, 0);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var outDir = bench.PathOf("o");
        var extracted = Arc.Extract(archive, new[] { "p/doc" }, outDir);
        Assert.True(extracted.IsSuccess, extracted.Error?.Message);
        Assert.True(File.Exists(Path.Combine(outDir, "doc", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(outDir, "docs")));
    }

    [Fact]
    public void ListDirBrowsesLikeAFolder()
    {
        using var bench = new Bench();
        var archive = bench.Built(ArchiveKind.Tar, 0);
        var entries = ArchiveIo.List(archive.Path).Value;
        Assert.Equal(new[] { "proj" }, Names(entries, ""));
        Assert.Equal(new[] { "proj/docs", "proj/empty", "proj/top.txt" }, Names(entries, "proj"));
        Assert.Equal(Names(entries, "proj"), Names(entries, "proj/"));
        Assert.Equal(new[] { "proj/docs/readme.txt", "proj/docs/sub" }, Names(entries, "proj/docs"));
        Assert.Empty(Names(entries, "proj/empty"));
        Assert.Empty(Names(entries, "nope"));
        Assert.Empty(Names(entries, "pro"));
    }

    [Fact]
    public void ListIncludesImpliedParentDirs()
    {
        using var bench = new Bench();
        var archive = bench.PathOf("x.zip");
        ArchiveSlip.WriteZip(archive, "a/b/c.txt", "c"u8.ToArray());
        var entries = ArchiveIo.List(archive).Value;
        var paths = new List<(string Path, bool IsDirectory)>();
        foreach (var entry in entries)
        {
            paths.Add((entry.Path, entry.IsDirectory));
        }

        Assert.Equal(new[] { ("a", true), ("a/b", true), ("a/b/c.txt", false) }, paths);
    }

    [Fact]
    public void UnsafeNamesAreRecognised()
    {
        foreach (var bad in new[] { "/etc/passwd", "\\\\server\\share\\x", "C:\\win\\x", "c:/x", "a/../../b", "..", "a\\..\\..\\b", "a\0b" })
        {
            Assert.NotNull(ArchivePaths.UnsafeReason(bad));
        }

        foreach (var good in new[] { "a/b.txt", "./a/b", "a/./b", "dir/", "weird..name", "ab:c/d" })
        {
            Assert.Null(ArchivePaths.UnsafeReason(good));
        }
    }

    [Fact]
    public void ZipSlipIsRejectedInZip()
    {
        foreach (var name in new[] { "../evil.txt", "/abs-evil.txt", "ok/../../evil.txt", "C:\\evil.txt" })
        {
            using var bench = new Bench();
            var archive = bench.PathOf("slip.zip");
            ArchiveSlip.WriteZip(archive, name, "pwned"u8.ToArray());
            bench.AssertSlipRejected(archive);
        }
    }

    [Fact]
    public void ZipSlipIsRejectedInTarAndTargz()
    {
        foreach (var gzip in new[] { false, true })
        {
            foreach (var name in new[] { "../evil.txt", "/abs-evil.txt", "a/../../evil.txt" })
            {
                using var bench = new Bench();
                var archive = bench.PathOf(gzip ? "s.tar.gz" : "s.tar");
                ArchiveSlip.WriteTar(archive, name, gzip);
                bench.AssertSlipRejected(archive);
            }
        }
    }

    [Fact]
    public void ZipSlipIsRejectedIn7z()
    {
        using var bench = new Bench();
        var archive = bench.PathOf("slip.7z");
        ArchiveSlip.WriteSevenZipSlip(archive);
        bench.AssertSlipRejected(archive);
    }

    [Fact]
    public void LinksInArchivesAreNotRecreated()
    {
        using var bench = new Bench();
        var archive = bench.PathOf("link.tar");
        ArchiveSlip.WriteTarLink(archive, "evil-link", "/etc");
        var outDir = bench.PathOf("out");
        Assert.Equal(0, Arc.Extract(archive, null, outDir).Value);
        Assert.False(PathExists(Path.Combine(outDir, "evil-link")));
        Assert.Single(ArchiveIo.List(archive).Value);
    }

    [Fact]
    public void CreateCancelRemovesThePartialArchive()
    {
        foreach (var kind in ArchiveKinds.Creatable())
        {
            using var bench = new Bench();
            bench.MakeTree();
            var archive = bench.ArchivePath(kind);
            var seen = 0;
            var result = Arc.Create(archive, kind, new[] { bench.PathOf("proj") }, 6, _ =>
            {
                seen++;
                return seen < 3;
            });
            Assert.False(result.IsSuccess, kind.ToString());
            Assert.Equal(ErrorKind.Cancelled, result.Error!.Kind);
            Assert.False(File.Exists(archive), kind.ToString());
            Assert.False(File.Exists(archive + ".part"), kind.ToString());
            Assert.False(File.Exists(bench.PathOf("out.part")));
            Assert.Single(Directory.GetFileSystemEntries(bench.Root));
        }
    }

    [Fact]
    public void ExtractCancelStopsEarly()
    {
        using var bench = new Bench();
        var archive = bench.Built(ArchiveKind.Zip, 6);
        var outDir = bench.PathOf("o");
        var calls = 0;
        var result = Arc.Extract(archive.Path, null, outDir, _ =>
        {
            calls++;
            return calls < 3;
        });
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value < 3);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void FailedCreateKeepsAnExistingArchive()
    {
        using var bench = new Bench();
        var archive = bench.PathOf("keep.zip");
        File.WriteAllText(archive, "old");
        var missing = bench.PathOf("does-not-exist");
        var result = Arc.Create(archive, ArchiveKind.Zip, new[] { missing }, 6);
        Assert.False(result.IsSuccess);
        Assert.Equal("old", File.ReadAllText(archive));
    }

    [Fact]
    public void TheArchiveIsNotStoredInItself()
    {
        using var bench = new Bench();
        var proj = bench.MakeTree();
        var archive = Path.Combine(proj, "self.tar");
        File.WriteAllText(archive, "old");
        var created = Arc.Create(archive, ArchiveKind.Tar, new[] { proj }, 0);
        Assert.Equal(3, created.Value);
        foreach (var entry in ArchiveIo.List(archive).Value)
        {
            Assert.NotEqual("proj/self.tar", entry.Path);
        }
    }

    private static string[] Names(IReadOnlyList<ArchiveEntry> entries, string inner)
    {
        var names = new List<string>();
        foreach (var entry in ArchiveIo.ListDirectory(entries, inner))
        {
            names.Add(entry.Path);
        }

        names.Sort(StringComparer.Ordinal);
        return names.ToArray();
    }

    private static long EncodingLength(string text) => System.Text.Encoding.UTF8.GetByteCount(text);

    private static long UnixSeconds(string path)
    {
        var utc = File.GetLastWriteTimeUtc(path);
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
