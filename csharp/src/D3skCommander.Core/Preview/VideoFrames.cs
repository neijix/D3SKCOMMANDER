using System.Globalization;

namespace D3skCommander.Core.Preview;

/// <summary>The size of one decoded frame in memory: its pixels and the padded rows the decoder writes.</summary>
public readonly record struct VideoFrameSize(int Width, int Height, int Pitch, int Lines)
{
    /// <summary>Bytes the frame buffer needs.</summary>
    public int Bytes => Pitch * Lines;
}

/// <summary>
/// The arithmetic of the video preview: the size frames are decoded at (scaled down to the pane, never up,
/// in 32-bit pixels with rows padded the way VLC wants them) and the time label under the picture.
/// </summary>
public static class VideoFrames
{
    /// <summary>Wider videos are decoded at this width: a preview pane never needs more.</summary>
    public const int MaxWidth = 1920;

    private const int Align = 32;

    /// <summary>The frame size for a video of <paramref name="width"/> × <paramref name="height"/>, at most <paramref name="maxWidth"/> wide.</summary>
    public static VideoFrameSize Fit(int width, int height, int maxWidth = MaxWidth)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        maxWidth = Math.Max(16, maxWidth);
        if (width > maxWidth)
        {
            height = Math.Max(1, (int)Math.Round(height * (double)maxWidth / width));
            width = maxWidth;
        }

        return new VideoFrameSize(width, height, Up(width * 4), Up(height));
    }

    /// <summary>"0:42 / 3:05", or with hours "1:02:03 / 2:00:00"; the length is left out while it is unknown.</summary>
    public static string Clock(TimeSpan position, TimeSpan length)
    {
        var hours = length.TotalHours >= 1 || position.TotalHours >= 1;
        var at = Format(position, hours);
        return length > TimeSpan.Zero ? at + " / " + Format(length, hours) : at;
    }

    private static string Format(TimeSpan time, bool hours)
    {
        time = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        return hours
            ? ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + time.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture)
            : ((int)time.TotalMinutes).ToString(CultureInfo.InvariantCulture) + time.ToString(@"\:ss", CultureInfo.InvariantCulture);
    }

    private static int Up(int value) => (value + Align - 1) / Align * Align;
}
