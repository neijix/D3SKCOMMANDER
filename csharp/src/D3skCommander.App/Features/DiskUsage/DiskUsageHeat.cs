using Avalonia.Media;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>Colours of the disk usage window: one per ring branch, shared by the list's bars.</summary>
internal static class DiskUsageHeat
{
    // Mid-tone, so they read on the light and on the dark theme.
    private static readonly Color[] Branches =
    [
        Color.Parse("#4F8FBF"),
        Color.Parse("#8E6FBF"),
        Color.Parse("#3FA796"),
        Color.Parse("#C8709A"),
        Color.Parse("#86B64E"),
        Color.Parse("#D98C4A"),
        Color.Parse("#5B7FD1"),
        Color.Parse("#B8A236"),
    ];

    /// <summary>The ring colour of branch number <paramref name="branch"/>, lighter the deeper the ring.</summary>
    public static Color ForBranch(int branch, int level)
    {
        var color = Branches[Math.Abs(branch) % Branches.Length];
        var lighten = Math.Clamp((level - 1) * 0.1, 0, 0.5);
        return Color.FromRgb(Mix(color.R, lighten), Mix(color.G, lighten), Mix(color.B, lighten));
    }

    public static Color Other { get; } = Color.Parse("#8A8F98");

    /// <summary>Black or white, whichever reads better on <paramref name="fill"/>.</summary>
    public static Color TextOn(Color fill) =>
        ((0.299 * fill.R) + (0.587 * fill.G) + (0.114 * fill.B)) > 150 ? Color.FromRgb(20, 20, 20) : Colors.White;

    private static byte Mix(byte channel, double toWhite) => (byte)Math.Round(channel + ((255 - channel) * toWhite));
}
