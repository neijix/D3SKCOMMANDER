using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace D3skCommander.App.Icons;

/// <summary>
/// Image thumbnails for the tile view: decoded at display width off the UI thread, kept in a small
/// least-recently-used cache. The file is read, never written; a file that does not decode is
/// remembered as "no thumbnail" so its tile keeps the kind icon.
/// </summary>
public sealed class ThumbnailCache
{
    public const int Capacity = 512;

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico"];

    private readonly Dictionary<string, LinkedListNode<Slot>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Slot> _order = new();
    private readonly Dictionary<string, List<Action<Bitmap?>>> _pending = new(StringComparer.Ordinal);

    public static ThumbnailCache Shared { get; } = new();

    public int Count => _map.Count;

    /// <summary>Whether a file name has an extension the tile view decodes.</summary>
    public static bool CanThumbnail(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        foreach (var candidate in Extensions)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string KeyOf(string path, DateTimeOffset? modified, int width) =>
        path + "|" + (modified?.UtcTicks ?? 0).ToString(CultureInfo.InvariantCulture) + "|"
        + width.ToString(CultureInfo.InvariantCulture);

    /// <summary>The cached thumbnail (null for a remembered failure); false when not decoded yet.</summary>
    public bool TryGet(string key, out Bitmap? bitmap)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }

        bitmap = null;
        return false;
    }

    /// <summary>
    /// Asks for a thumbnail; <paramref name="done"/> runs on the UI thread, at once when cached.
    /// Several requests for one key share one decode. Call from the UI thread.
    /// </summary>
    public void Request(string path, DateTimeOffset? modified, int width, Action<Bitmap?> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var key = KeyOf(path, modified, width);
        if (TryGet(key, out var cached))
        {
            done(cached);
            return;
        }

        if (_pending.TryGetValue(key, out var waiting))
        {
            waiting.Add(done);
            return;
        }

        _pending[key] = [done];
        _ = Task.Run(() =>
        {
            var bitmap = Decode(path, width);
            Dispatcher.UIThread.Post(() => Finish(key, bitmap));
        });
    }

    private static Bitmap? Decode(string path, int width)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private void Finish(string key, Bitmap? bitmap)
    {
        Store(key, bitmap);
        if (!_pending.Remove(key, out var waiting))
        {
            return;
        }

        foreach (var done in waiting)
        {
            done(bitmap);
        }
    }

    internal void Store(string key, Bitmap? bitmap)
    {
        _map[key] = _order.AddFirst(new Slot(key, bitmap));
        while (_map.Count > Capacity && _order.Last is not null)
        {
            _map.Remove(_order.Last.Value.Key);
            _order.RemoveLast();
        }
    }

    private sealed record Slot(string Key, Bitmap? Bitmap);
}
