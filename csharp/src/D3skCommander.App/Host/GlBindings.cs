using Hexa.NET.GLFW;
using HexaGen.Runtime;

namespace D3skCommander.App.Host;

/// <summary>OpenGL entry points resolved through the GLFW window. The official Hexa.NET host uses the same shape.</summary>
internal sealed unsafe class GlBindings : IGLContext
{
    private readonly GLFWwindowPtr _window;

    public GlBindings(GLFWwindowPtr window) => _window = window;

    public nint Handle => (nint)_window.Handle;

    public bool IsCurrent => GLFW.GetCurrentContext() == _window;

    public void Dispose()
    {
    }

    public nint GetProcAddress(string procName) => (nint)GLFW.GetProcAddress(procName);

    public bool IsExtensionSupported(string extensionName) => GLFW.ExtensionSupported(extensionName) != 0;

    public void MakeCurrent() => GLFW.MakeContextCurrent(_window);

    public void SwapBuffers() => GLFW.SwapBuffers(_window);

    public void SwapInterval(int interval) => GLFW.SwapInterval(interval);

    public bool TryGetProcAddress(string procName, out nint procAddress)
    {
        procAddress = (nint)GLFW.GetProcAddress(procName);
        return procAddress != 0;
    }
}
