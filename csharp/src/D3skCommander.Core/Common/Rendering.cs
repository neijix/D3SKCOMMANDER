namespace D3skCommander.Core.Common;

/// <summary>How the window draws: the platform's choice (GPU when it works) or the CPU only.</summary>
public enum RenderingChoice
{
    /// <summary>The platform picks (OpenGL, Metal, ANGLE…, with its own software fallback).</summary>
    Auto,

    /// <summary>The CPU draws every frame: no GPU, driver or OpenGL needed.</summary>
    Software,
}

/// <summary>
/// Reads <c>D3SK_RENDERING</c>: <c>software</c> (any case) forces CPU drawing, for a machine whose
/// GPU driver crashes or draws garbage, a virtual machine or a remote desktop; anything else is
/// <see cref="RenderingChoice.Auto"/>.
/// </summary>
public static class Rendering
{
    /// <summary>The variable read at start.</summary>
    public const string Variable = "D3SK_RENDERING";

    /// <summary>The choice <paramref name="value"/> (the variable's text, or null when unset) asks for.</summary>
    public static RenderingChoice Parse(string? value) =>
        string.Equals(value?.Trim(), "software", StringComparison.OrdinalIgnoreCase) ? RenderingChoice.Software : RenderingChoice.Auto;

    /// <summary>The choice the environment asks for.</summary>
    public static RenderingChoice FromEnvironment() => Parse(Environment.GetEnvironmentVariable(Variable));
}
