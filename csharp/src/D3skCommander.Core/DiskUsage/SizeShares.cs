namespace D3skCommander.Core.DiskUsage;

/// <summary>Pure helpers for the percentage column and the heat bar.</summary>
public static class SizeShares
{
    public const int HeatSteps = 6;

    /// <summary>The part of <paramref name="whole"/> that <paramref name="part"/> makes, 0 to 100. 0 for an empty whole.</summary>
    public static double Percent(long part, long whole)
    {
        if (whole <= 0 || part <= 0)
        {
            return 0;
        }

        return Math.Min(100, part * 100.0 / whole);
    }

    /// <summary>"12.3 %", "&lt; 0.1 %" for a tiny non-empty share.</summary>
    public static string PercentText(long part, long whole)
    {
        var percent = Percent(part, whole);
        if (percent <= 0)
        {
            return "0 %";
        }

        return percent < 0.1
            ? "< 0.1 %"
            : percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " %";
    }

    /// <summary>Maps a share (0 to 1) to a step, 0 (cool) up to <paramref name="steps"/> - 1 (warm).</summary>
    public static int HeatStep(double fraction, int steps = HeatSteps)
    {
        if (steps < 2 || double.IsNaN(fraction) || fraction <= 0)
        {
            return 0;
        }

        return Math.Min(steps - 1, (int)Math.Floor(fraction * steps));
    }
}
