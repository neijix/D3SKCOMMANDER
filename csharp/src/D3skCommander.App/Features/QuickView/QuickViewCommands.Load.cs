using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Host;
using D3skCommander.App.Imaging;
using D3skCommander.Core.Archives;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Preview;
using LibVLCSharp.Shared;
using CoreDocument = D3skCommander.Core.TextDocument.TextDocument;

namespace D3skCommander.App.Features.QuickView;

/// <summary>
/// The file the view shows, by what it holds (<see cref="PreviewKinds"/>): text in the editor, a picture or a
/// camera RAW's embedded preview, a video or a sound (with the VLC of the machine), the pages of a PDF, the list of an archive, and the lister for the rest.
/// Every read runs in the background; a result that comes back after the cursor moved on is dropped.
/// </summary>
public sealed partial class QuickViewCommands
{
    /// <summary>A RAW preview or a picture is scaled down to this width at most.</summary>
    public const int PictureWidth = 2560;

    /// <summary>A PDF shows its first pages only.</summary>
    public const int MaxPages = 50;

    private const int PageWidthFallback = 800;

    /// <summary>The libvlc videos play with; null without VLC (tests set their own).</summary>
    internal Func<LibVLC?> Vlc { get; set; } = VlcRuntime.Shared;

    /// <summary>Seconds into the file videos and sounds start at (the preferences; tests set their own).</summary>
    internal Func<int>? StartSecondsOverride { get; set; }

    private int StartSeconds() => StartSecondsOverride?.Invoke() ?? _settings?.Preferences.Current.PreviewStartSeconds ?? 0;

    private bool Current(int ticket) => ticket == _ticket && _panel is not null;

    private async Task PreviewAsync(string path, int ticket)
    {
        var kind = await Task.Run(() => PreviewKinds.Of(path)).ConfigureAwait(true);
        if (!Current(ticket))
        {
            return;
        }

        switch (kind)
        {
            case PreviewKind.Text:
                await ShowTextAsync(path, ticket).ConfigureAwait(true);
                break;
            case PreviewKind.Picture:
                await ShowPictureAsync(path, ticket, () => PictureDecoder.Decode(path)).ConfigureAwait(true);
                break;
            case PreviewKind.CameraRaw:
                await ShowPictureAsync(path, ticket, () => DecodeRaw(path)).ConfigureAwait(true);
                break;
            case PreviewKind.Archive:
                await ShowArchiveAsync(path, ticket).ConfigureAwait(true);
                break;
            case PreviewKind.Pdf:
                await ShowPdfAsync(path, ticket).ConfigureAwait(true);
                break;
            case PreviewKind.Video:
                await ShowVideoAsync(path, ticket, audio: false).ConfigureAwait(true);
                break;
            case PreviewKind.Audio:
                await ShowVideoAsync(path, ticket, audio: true).ConfigureAwait(true);
                break;
            default:
                InLister(path, ticket);
                break;
        }
    }

    // The lister: hex, binary text, or its own message for a file it cannot show.
    private void InLister(string path, int ticket) => UiThread.Post(() =>
    {
        if (Current(ticket) && _viewer is not null)
        {
            _panel!.ShowViewer();
            _ = _viewer.OpenAsync(path);
        }
    });

    private async Task ShowTextAsync(string path, int ticket)
    {
        if (_dialogs is not null && await EditTextAsync(path, ticket).ConfigureAwait(true))
        {
            return;
        }

        var text = await Task.Run(() => TextPreview.Read(path)).ConfigureAwait(true);
        if (text is null)
        {
            InLister(path, ticket);
            return;
        }

        UiThread.Post(() =>
        {
            if (Current(ticket))
            {
                _panel!.ShowText(text, path);
            }
        });
    }

    // The editable view: true when the editor shows the file (or the cursor moved on meanwhile); false for a
    // file the editor will not take (too big, unreadable, invalid UTF-8), which the read-only view shows instead.
    private async Task<bool> EditTextAsync(string path, int ticket)
    {
        var document = await Task.Run(() => CoreDocument.Open(path)).ConfigureAwait(true);
        if (document.TooBig || document.Refused is not null || document.Lossy)
        {
            return false;
        }

        UiThread.Invoke(() =>
        {
            if (Current(ticket))
            {
                var model = new EditorViewModel(_text, _dialogs!, () => _panels.RefreshAsync());
                model.Load(document);
                _edited = model;
                _panel!.ShowEditor(model);
            }
        });
        return true;
    }

    private async Task ShowPictureAsync(string path, int ticket, Func<Bitmap?> decode)
    {
        var picture = await Task.Run(decode).ConfigureAwait(true);
        if (picture is null)
        {
            InLister(path, ticket);
            return;
        }

        UiThread.Post(() =>
        {
            if (Current(ticket))
            {
                _panel!.ShowPicture(picture);
            }
            else
            {
                picture.Dispose();
            }
        });
    }

    // The JPEG a camera keeps inside its RAW file; a DNG without one may still decode as a TIFF.
    private static Bitmap? DecodeRaw(string path) =>
        CameraRaw.Read(path) is { } preview
            ? PictureDecoder.DecodeJpeg(preview.Jpeg, preview.Orientation, PictureWidth)
            : PictureDecoder.Decode(path, PictureWidth);

    private async Task ShowArchiveAsync(string path, int ticket)
    {
        var listed = await Task.Run(() => ArchiveIo.List(path)).ConfigureAwait(true);
        var rows = listed.IsSuccess ? PreviewViews.RowsOf(listed.Value) : null;
        UiThread.Post(() =>
        {
            if (!Current(ticket))
            {
                return;
            }

            if (rows is null)
            {
                _panel!.ShowSummary(_text.Get("quickview.archive_error"));
                return;
            }

            var files = listed.Value.Where(entry => !entry.IsDirectory).ToList();
            var size = (ulong)files.Sum(entry => Math.Max(0, entry.Size));
            var header = _text.Get("quickview.archive_count")
                .Replace("{files}", files.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{size}", LocalFileSystem.FormatSize(size), StringComparison.Ordinal);
            _panel!.ShowArchive(header, rows);
        });
    }

    // The first video or sound loads libvlc, which takes a moment: off the UI thread. Without VLC, a line says to install it.
    private async Task ShowVideoAsync(string path, int ticket, bool audio)
    {
        var vlc = await Task.Run(Vlc).ConfigureAwait(true);
        UiThread.Post(() =>
        {
            if (!Current(ticket))
            {
                return;
            }

            if (vlc is null)
            {
                _panel!.ShowSummary(_text.Get("quickview.video_no_vlc"));
                return;
            }

            _panel!.ShowVideo(new VideoPreview(vlc, _text, path, PageWidth(), StartSeconds(), audio));
        });
    }

    private async Task ShowPdfAsync(string path, int ticket)
    {
        using var pdf = await Task.Run(() => PdfPages.Open(path)).ConfigureAwait(true);
        if (!Current(ticket))
        {
            return;
        }

        if (pdf is null || pdf.Count == 0)
        {
            UiThread.Post(() =>
            {
                if (Current(ticket))
                {
                    _panel!.ShowSummary(_text.Get("quickview.pdf_error"));
                }
            });
            return;
        }

        var width = PageWidth();
        UiThread.Post(() =>
        {
            if (Current(ticket))
            {
                _panel!.StartPages();
            }
        });
        for (var index = 0; index < Math.Min(pdf.Count, MaxPages) && Current(ticket); index++)
        {
            var page = index;
            var raster = await Task.Run(() => pdf.Render(page, width)).ConfigureAwait(true);
            if (raster is null)
            {
                continue;
            }

            UiThread.Post(() =>
            {
                if (Current(ticket))
                {
                    _panel!.AddPage(PictureDecoder.FromRaster(raster, PixelFormats.Bgra8888));
                }
            });
        }
    }

    // The pane's width in device pixels, less the page margins, so a page is drawn sharp at its size on screen.
    private int PageWidth()
    {
        var panel = _panel!;
        var scaling = TopLevel.GetTopLevel(panel)?.RenderScaling ?? 1;
        var width = panel.Bounds.Width - 28;
        return width < 100 ? PageWidthFallback : Math.Min(4000, (int)(width * scaling));
    }
}
