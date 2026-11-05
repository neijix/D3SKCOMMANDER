using D3skCommander.Core.Common;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;
using ScZip = SharpCompress.Archives.Zip.ZipArchive;
using ZipEntry = SharpCompress.Archives.Zip.ZipArchiveEntry;

namespace D3skCommander.Core.Archives;

internal static class ArchiveZip
{
    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List(string path) =>
        ArchiveFlow.List(path, Open, ViewsOf);

    public static ArchiveResult<int> Extract(
        string path, ArchivePlan plan, string destination,
        Func<string, bool>? progress, CancellationToken cancellationToken) =>
        ArchiveFlow.Extract(path, Open, ViewsOf, plan, destination, progress, cancellationToken);

    private static IArchive Open(string path) => ScZip.OpenArchive(path);

    private static IEnumerable<MemberView> ViewsOf(IArchive archive) => Views(archive.Entries);

    public static ArchiveResult<int> Create(
        string path,
        IReadOnlyList<SourceItem> items,
        int level,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(level, 0, 9);
        // SharpCompress 0.50.8 writes a directory's local header with the writer's method and the
        // central header as Store. A Deflate writer makes 7z reject the zip. None keeps the two
        // headers in agreement; each file still sets its own method in Write.
        var options = new ZipWriterOptions(CompressionType.None)
        {
            CompressionLevel = 0,
        };
        using var stream = File.Create(path);
        using var writer = new ZipWriter(stream, options);
        return ArchiveFlow.Write(items, progress, cancellationToken, item => Write(writer, item, clamped));
    }

    private static IEnumerable<MemberView> Views(IEnumerable<IArchiveEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is ZipEntry zip)
            {
                yield return View(zip);
            }
        }
    }

    private static MemberView View(ZipEntry entry)
    {
        var link = ArchiveLinks.Zip(entry);
        var raw = entry.Key ?? "";
        var directory = !link && (entry.IsDirectory || raw.EndsWith('/') || raw.EndsWith('\\'));
        Func<Stream>? open = null;
        if (!directory && !link)
        {
            open = entry.OpenEntryStream;
        }

        return new MemberView(
            raw,
            directory,
            link,
            entry.Size,
            entry.CompressedSize,
            entry.LastModifiedTime,
            ArchiveLinks.ZipMode(entry),
            open);
    }

    private static void Write(ZipWriter writer, SourceItem item, int level)
    {
        if (item.IsDirectory)
        {
            writer.WriteDirectory(item.Name, item.Modified);
            return;
        }

        using var input = File.OpenRead(item.DiskPath);
        writer.Write(item.Name, input, new ZipWriterEntryOptions
        {
            ModificationDateTime = item.Modified,
            CompressionType = level == 0 ? CompressionType.None : CompressionType.Deflate,
            CompressionLevel = level,
        });
    }
}
