using System.Runtime.InteropServices;
using D3skCommander.Core.Viewer;

namespace D3skCommander.Core.Preview;

/// <summary>
/// A PDF opened for the preview, its pages drawn by PDFium (the bblanchon.PDFium native packages, one
/// per OS). PDFium is not thread-safe: every call goes through one lock. Pages come back as BGRA pixels
/// on white, <see cref="RasterImage.Rgba"/> holding them in that order.
/// </summary>
public sealed class PdfPages : IDisposable
{
    /// <summary>Larger files are not loaded for a preview.</summary>
    public const long MaxBytes = 300L * 1024 * 1024;

    private const int Bgra = 4;
    private const int Annotations = 0x01;
    private static readonly Lock Gate = new();
    private static bool _started;

    private readonly byte[] _data;
    private GCHandle _pinned;
    private IntPtr _document;

    private PdfPages(byte[] data, GCHandle pinned, IntPtr document, int count)
    {
        _data = data;
        _pinned = pinned;
        _document = document;
        Count = count;
    }

    public int Count { get; }

    /// <summary>The PDF at <paramref name="path"/>; null when it cannot be read, is protected, or PDFium is missing.</summary>
    public static PdfPages? Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] data;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes)
            {
                return null;
            }

            data = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or OutOfMemoryException)
        {
            return null;
        }

        return Load(data);
    }

    /// <summary>The PDF held in <paramref name="data"/>; null when it does not load.</summary>
    public static PdfPages? Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (Gate)
        {
            if (!Start())
            {
                return null;
            }

            var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            var document = Native.LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)data.Length, null);
            if (document == IntPtr.Zero)
            {
                pinned.Free();
                return null;
            }

            return new PdfPages(data, pinned, document, Native.GetPageCount(document));
        }
    }

    /// <summary>Page <paramref name="index"/> drawn <paramref name="width"/> pixels wide; null when it does not draw.</summary>
    public RasterImage? Render(int index, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
            if (index >= Count)
            {
                return null;
            }

            var page = Native.LoadPage(_document, index);
            if (page == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Draw(page, width);
            }
            finally
            {
                Native.ClosePage(page);
            }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_document == IntPtr.Zero)
            {
                return;
            }

            Native.CloseDocument(_document);
            _document = IntPtr.Zero;
            _pinned.Free();
            GC.KeepAlive(_data);
        }
    }

    private static RasterImage? Draw(IntPtr page, int width)
    {
        var pageWidth = Native.GetPageWidthF(page);
        var pageHeight = Native.GetPageHeightF(page);
        if (pageWidth <= 0 || pageHeight <= 0)
        {
            return null;
        }

        var height = Math.Clamp((int)Math.Round(width * pageHeight / pageWidth), 1, 20000);
        var pixels = new byte[width * height * 4];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var bitmap = Native.BitmapCreateEx(width, height, Bgra, pinned.AddrOfPinnedObject(), width * 4);
            if (bitmap == IntPtr.Zero)
            {
                return null;
            }

            _ = Native.BitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            Native.RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, Annotations);
            Native.BitmapDestroy(bitmap);
        }
        finally
        {
            pinned.Free();
        }

        return new RasterImage(width, height, pixels);
    }

    // The native library is loaded on first use; a machine or build without it previews no PDF.
    private static bool Start()
    {
        if (_started)
        {
            return true;
        }

        try
        {
            Native.InitLibrary();
            _started = true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }

        return true;
    }

    // DllImport: LibraryImport needs AllowUnsafeBlocks, which this library does not set.
    private static class Native
    {
        private const string Library = "pdfium";

        [DllImport(Library, EntryPoint = "FPDF_InitLibrary")]
        public static extern void InitLibrary();

        [DllImport(Library, EntryPoint = "FPDF_LoadMemDocument64")]
        public static extern IntPtr LoadMemDocument64(IntPtr data, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

        [DllImport(Library, EntryPoint = "FPDF_CloseDocument")]
        public static extern void CloseDocument(IntPtr document);

        [DllImport(Library, EntryPoint = "FPDF_GetPageCount")]
        public static extern int GetPageCount(IntPtr document);

        [DllImport(Library, EntryPoint = "FPDF_LoadPage")]
        public static extern IntPtr LoadPage(IntPtr document, int index);

        [DllImport(Library, EntryPoint = "FPDF_ClosePage")]
        public static extern void ClosePage(IntPtr page);

        [DllImport(Library, EntryPoint = "FPDF_GetPageWidthF")]
        public static extern float GetPageWidthF(IntPtr page);

        [DllImport(Library, EntryPoint = "FPDF_GetPageHeightF")]
        public static extern float GetPageHeightF(IntPtr page);

        [DllImport(Library, EntryPoint = "FPDFBitmap_CreateEx")]
        public static extern IntPtr BitmapCreateEx(int width, int height, int format, IntPtr firstScan, int stride);

        [DllImport(Library, EntryPoint = "FPDFBitmap_FillRect")]
        public static extern int BitmapFillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

        [DllImport(Library, EntryPoint = "FPDF_RenderPageBitmap")]
        public static extern void RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);

        [DllImport(Library, EntryPoint = "FPDFBitmap_Destroy")]
        public static extern void BitmapDestroy(IntPtr bitmap);
    }
}
