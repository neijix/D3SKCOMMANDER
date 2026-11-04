namespace D3skCommander.App.Smoke;

/// <summary>The host installs a reader once the OpenGL framebuffer exists.</summary>
internal static class Framebuffer
{
    public static Func<(int Width, int Height, byte[] Rgba)>? Read { get; set; }
}
