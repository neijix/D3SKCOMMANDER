using D3skCommander.Core.Common;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;
using ScSeven = SharpCompress.Archives.SevenZip.SevenZipArchive;
using SzEntry = SharpCompress.Archives.SevenZip.SevenZipArchiveEntry;

namespace D3skCommander.Core.Archives;

internal static class ArchiveSevenZip
{
    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List(string path) =>
        ArchiveFlow.List(path, Open, ViewsOf);

    public static ArchiveResult<int> Extract(
        string path, ArchivePlan plan, string destination,
        Func<string, bool>? progress, CancellationToken cancellationToken) =>
        ArchiveFlow.Extract(path, Open, ViewsOf, plan, destination, progress, cancellationToken);

    private static IArchive Open(string path) => ScSeven.OpenArchive(path);

    private static IEnumerable<MemberView> ViewsOf(IArchive archive) => Views(archive.Entries);

    public static ArchiveResult<int> Create(
        string path,
        IReadOnlyList<SourceItem> items,
        int level,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        // SevenZipWriter accepts LZMA and LZMA2 only, so level 0 is still LZMA2 at that level.
        var clamped = Math.Clamp(level, 0, 9);
        var options = new SevenZipWriterOptions(CompressionType.LZMA2) { CompressionLevel = clamped };
        using var stream = File.Create(path);
        using var writer = new SevenZipWriter(stream, options);
        return ArchiveFlow.Write(items, progress, cancellationToken, item => Write(writer, item));
    }

    private static IEnumerable<MemberView> Views(IEnumerable<IArchiveEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is SzEntry seven && !seven.IsAnti)
            {
                yield return View(seven);
            }
        }
    }

    private static MemberView View(SzEntry entry)
    {
        var raw = entry.Key ?? "";
        var link = ArchiveLinks.SevenZip(entry);
        var directory = !link && (entry.IsDirectory || raw.EndsWith('/') || raw.EndsWith('\\'));
        long? compressed = entry.CompressedSize > 0 ? entry.CompressedSize : null;
        Func<Stream>? open = null;
        if (!directory && !link)
        {
            open = entry.OpenEntryStream;
        }

        return new MemberView(raw, directory, link, entry.Size, compressed, entry.LastModifiedTime, null, open);
    }

    private static void Write(SevenZipWriter writer, SourceItem item)
    {
        if (item.IsDirectory)
        {
            writer.WriteDirectory(item.Name, item.Modified);
            return;
        }

        using var input = File.OpenRead(item.DiskPath);
        writer.Write(item.Name, input, item.Modified);
    }
}
