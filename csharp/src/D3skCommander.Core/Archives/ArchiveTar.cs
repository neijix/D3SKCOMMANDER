using System.IO.Compression;
using D3skCommander.Core.Common;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Common.Tar;
using SharpCompress.Writers.Tar;
using ScTar = SharpCompress.Archives.Tar.TarArchive;

namespace D3skCommander.Core.Archives;

internal static class ArchiveTar
{
    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List(string path, bool gzip)
    {
        string? temp = null;
        try
        {
            using var holder = Open(path, gzip, CancellationToken.None, out temp);
            var entries = new List<ArchiveEntry>();
            foreach (var entry in holder.Entries)
            {
                ArchiveExtract.AddListed(entries, View(entry));
            }

            return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Ok(ArchivePaths.WithImpliedDirectories(entries));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Fail(ArchiveErrors.From(ex, path));
        }
        finally
        {
            ArchiveErrors.Delete(temp ?? "");
        }
    }

    public static ArchiveResult<int> Extract(
        string path,
        bool gzip,
        ArchivePlan plan,
        string destination,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        string? temp = null;
        try
        {
            using var holder = Open(path, gzip, cancellationToken, out temp);
            return ArchiveExtract.Read(Views(holder), plan, destination, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ArchiveResult<int>.Fail(ArchiveErrors.From(ex, path));
        }
        finally
        {
            ArchiveErrors.Delete(temp ?? "");
        }
    }

    public static ArchiveResult<int> Create(
        string path,
        bool gzip,
        IReadOnlyList<SourceItem> items,
        int level,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(level, 0, 9);
        var type = gzip ? CompressionType.GZip : CompressionType.None;
        using var stream = File.Create(path);
        using var writer = new TarWriter(stream, new TarWriterOptions(type, true) { CompressionLevel = clamped });
        return ArchiveFlow.Write(items, progress, cancellationToken, item => Write(writer, item));
    }

    private static TarHolder Open(string path, bool gzip, CancellationToken cancellationToken, out string? temp)
    {
        temp = null;
        if (!gzip)
        {
            var opened = ScTar.OpenArchive(path);
            return new TarHolder(opened, opened.Entries, null);
        }

        // SharpCompress reads tar only from a seekable stream, so the gzip layer is inflated first.
        temp = Path.Combine(Path.GetTempPath(), "d3sk-tgz-" + Guid.NewGuid().ToString("N"));
        try
        {
            Inflate(path, temp, cancellationToken);
            var opened = ScTar.OpenArchive(temp);
            return new TarHolder(opened, opened.Entries, temp);
        }
        catch
        {
            ArchiveErrors.Delete(temp);
            temp = null;
            throw;
        }
    }

    private static void Inflate(string path, string temp, CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(path);
        using var decoded = new GZipStream(input, CompressionMode.Decompress);
        using var output = File.Create(temp);
        var buffer = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = decoded.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return;
            }

            output.Write(buffer, 0, read);
        }
    }

    private static IEnumerable<MemberView> Views(TarHolder holder)
    {
        foreach (var entry in holder.Entries)
        {
            yield return View(entry);
        }
    }

    private static MemberView View(IArchiveEntry entry)
    {
        var tar = (TarEntry)entry;
        var raw = tar.Key ?? "";
        var link = !string.IsNullOrEmpty(tar.LinkTarget);
        var directory = !link && (tar.IsDirectory || raw.EndsWith('/') || raw.EndsWith('\\'));
        Func<Stream>? open = null;
        if (!directory && !link)
        {
            open = entry.OpenEntryStream;
        }

        return new MemberView(
            raw,
            directory,
            link,
            tar.Size,
            null,
            tar.LastModifiedTime,
            unchecked((uint)tar.Mode),
            open);
    }

    private static void Write(TarWriter writer, SourceItem item)
    {
        if (item.IsDirectory)
        {
            writer.WriteDirectory(item.Name, item.Modified);
            return;
        }

        using var input = File.OpenRead(item.DiskPath);
        writer.Write(item.Name, input, item.Modified, item.Size);
    }
}

internal sealed class TarHolder : IDisposable
{
    private readonly IDisposable _archive;
    private readonly string? _temp;

    public TarHolder(IDisposable archive, IEnumerable<IArchiveEntry> entries, string? temp)
    {
        _archive = archive;
        Entries = entries;
        _temp = temp;
    }

    public IEnumerable<IArchiveEntry> Entries { get; }

    public void Dispose()
    {
        _archive.Dispose();
        if (_temp is not null)
        {
            ArchiveErrors.Delete(_temp);
        }
    }
}
