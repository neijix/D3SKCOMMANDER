using D3skCommander.Core.Archives;
using D3skCommander.Core.Viewer;

namespace D3skCommander.Core.Preview;

/// <summary>How the preview panel shows a file.</summary>
public enum PreviewKind
{
    /// <summary>Anything else: the lister (bytes, tables).</summary>
    Other,

    /// <summary>Text with line numbers and, when the extension is known, colours.</summary>
    Text,

    /// <summary>A photo the app decodes (PNG, JPEG, GIF, BMP, WebP, ICO, TIFF).</summary>
    Picture,

    /// <summary>A camera RAW: the JPEG the camera stored inside it.</summary>
    CameraRaw,

    /// <summary>A ZIP, 7Z, TAR or another archive: the list of what it holds.</summary>
    Archive,

    /// <summary>A PDF: its pages.</summary>
    Pdf,

    /// <summary>A film or a clip, played by the VLC installed on the machine.</summary>
    Video,
}

/// <summary>
/// Picks the <see cref="PreviewKind"/> of a file from its name and first bytes. Text is any file up to
/// <see cref="TextLimit"/> whose head has no zero byte (or starts with a UTF-16 mark).
/// </summary>
public static class PreviewKinds
{
    /// <summary>Bytes of the file's head <see cref="Of(string, long, ReadOnlySpan{byte})"/> looks at.</summary>
    public const int HeadLength = 8192;

    /// <summary>Larger text files go to the lister, which pages them.</summary>
    public const long TextLimit = 4L * 1024 * 1024;

    private static readonly string[] RawExtensions =
    [
        "raw", "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "dng", "orf", "rw2", "raf", "pef", "srw",
        "x3f", "3fr", "iiq", "rwl", "erf", "mef", "mos", "kdc", "dcr",
    ];

    private static readonly string[] VideoExtensions =
    [
        "mp4", "m4v", "mkv", "webm", "avi", "mov", "wmv", "asf", "flv", "mpg", "mpeg", "m2v", "m2ts", "mts",
        "vob", "3gp", "3g2", "ogv", "divx", "rm", "rmvb", "f4v",
    ];

    /// <summary>True for a file name whose extension says it is a video.</summary>
    public static bool IsVideo(string path) => Array.IndexOf(VideoExtensions, ViewerFiles.Extension(path)) >= 0;

    public static PreviewKind Of(string path, long size, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = ViewerFiles.Extension(path);
        if (head.StartsWith("%PDF"u8) || extension == "pdf")
        {
            return PreviewKind.Pdf;
        }

        if (Array.IndexOf(RawExtensions, extension) >= 0)
        {
            return PreviewKind.CameraRaw;
        }

        if (PictureSignature.Of(head) != PictureFormat.Unknown || ViewerFiles.IsImage(path))
        {
            return PreviewKind.Picture;
        }

        if (IsVideo(path) || (extension == "ts" && IsTransportStream(head)))
        {
            return PreviewKind.Video;
        }

        if (ArchiveKinds.FromPath(path) is not null)
        {
            return PreviewKind.Archive;
        }

        return size <= TextLimit && LooksLikeText(head) ? PreviewKind.Text : PreviewKind.Other;
    }

    /// <summary>The kind of the file at <paramref name="path"/>; Other when it cannot be read.</summary>
    public static PreviewKind Of(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[HeadLength];
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return Of(path, stream.Length, head.AsSpan(0, read));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return PreviewKind.Other;
        }
    }

    // A .ts file is a TypeScript source as often as an MPEG transport stream: the stream's packets start with 0x47.
    private static bool IsTransportStream(ReadOnlySpan<byte> head) =>
        head.Length > 0 && head[0] == 0x47 && (head.Length <= 188 || head[188] == 0x47);

    private static bool LooksLikeText(ReadOnlySpan<byte> head) =>
        head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
        || !head.Contains((byte)0);
}
