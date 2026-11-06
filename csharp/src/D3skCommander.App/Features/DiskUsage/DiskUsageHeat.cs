using Avalonia.Media;
using D3skCommander.Core.DiskUsage;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>Colours of the disk usage window: a cool-to-warm scale for shares, a calmer set for the ring branches.</summary>
internal static class DiskUsageHeat
{
    // Mid-tone, so they read on the light and on the dark theme.
    private static readonly Color[] Scale =
    [
        Color.Parse("#4F8FBF"),
        Color.Parse("#3FA796"),
        Color.Parse("#86B64E"),
        Color.Parse("#D6B73C"),
        Color.Parse("#E58A3A"),
        Color.Parse("#D4503F"),
    ];

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

    private static readonly IBrush[] ScaleBrushes = [.. Scale.Select(color => (IBrush)new SolidColorBrush(color).ToImmutable())];

    /// <summary>The bar brush for a share of its parent (0 to 1).</summary>
    public static IBrush ForShare(double fraction) =>
        ScaleBrushes[SizeShares.HeatStep(fraction, ScaleBrushes.Length)];

    /// <summary>The ring colour of branch number <paramref name="branch"/>, lighter the deeper the ring.</summary>
    public static Color ForBranch(int branch, int level)
    {
        var color = Branches[Math.Abs(branch) % Branches.Length];
        var lighten = Math.Clamp((level - 1) * 0.1, 0, 0.5);
        return Color.FromRgb(Mix(color.R, lighten), Mix(color.G, lighten), Mix(color.B, lighten));
    }

    public static Color Other { get; } = Color.Parse("#8A8F98");

    private static byte Mix(byte channel, double toWhite) => (byte)Math.Round(channel + ((255 - channel) * toWhite));
}
