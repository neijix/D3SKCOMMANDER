using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Smoke;
using D3skCommander.App.ToolWindows;
using Hexa.NET.GLFW;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Utilities;
using Hexa.NET.ImPlot;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using Microsoft.Extensions.DependencyInjection;
using GLFWmonitorPtr = Hexa.NET.GLFW.GLFWmonitorPtr;
using GLFWwindowPtr = Hexa.NET.GLFW.GLFWwindowPtr;

namespace D3skCommander.App.Host;

/// <summary>The desktop window: GLFW, an OpenGL 3.2 core context, and Hexa.NET.ImGui.</summary>
public sealed class GlfwOpenGlHost : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly NativeCallback<GLFWerrorfun> _error;
    private bool _disposed;

    public unsafe GlfwOpenGlHost(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _error = new NativeCallback<GLFWerrorfun>(static (code, description) =>
        {
            _ = code;
            var message = Utils.DecodeStringUTF8(description);
            if (message.Contains("does not provide the window position", StringComparison.Ordinal))
            {
                return;
            }

            Console.Error.WriteLine(message);
        });
    }

    public unsafe void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UiThread.Install(Thread.CurrentThread);
        GLFW.SetErrorCallback(_error);
        if (GLFW.Init() == 0)
        {
            throw new InvalidOperationException("GLFW did not start.");
        }

        var window = CreateWindow(out var scale);
        if (window.IsNull)
        {
            GLFW.Terminate();
            throw new InvalidOperationException("GLFW did not open a window.");
        }

        GLFW.MakeContextCurrent(window);
        var gui = ImGui.CreateContext();
        var plot = ImPlot.CreateContext();
        ImGui.SetCurrentContext(gui);
        ImPlot.SetCurrentContext(plot);
        ImGuiContextFactory.Configure(scale, viewports: ViewportsFitThisPlatform());
        var io = ImGui.GetIO();
        if (!StartBackends(window, gui))
        {
            Destroy(window, gui, plot, backends: false);
            throw new InvalidOperationException("The ImGui GLFW or OpenGL3 backend did not start.");
        }

        using var gl = new GL(new GlBindings(window));
        Framebuffer.Read = () => ReadPixels(gl, window);
        var shell = _services.GetRequiredService<MainWindow>();
        GLFW.SetWindowTitle(window, shell.Title);
        shell.Show();
        Loop(window, gl, io, shell);
        Framebuffer.Read = null;
        Destroy(window, gui, plot, backends: true);
    }

    public void Dispose() => _disposed = true;

    /// <summary>Wayland does not report window positions, so ImGui viewports cannot place tool windows.</summary>
    private static bool ViewportsFitThisPlatform()
    {
        var platform = Environment.GetEnvironmentVariable("GLFW_PLATFORM");
        if (string.Equals(platform, "x11", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "win32", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "cocoa", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }

    private static unsafe GLFWwindowPtr CreateWindow(out float scale)
    {
        GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MAJOR, 3);
        GLFW.WindowHint(GLFW.GLFW_CONTEXT_VERSION_MINOR, 2);
        GLFW.WindowHint(GLFW.GLFW_OPENGL_PROFILE, GLFW.GLFW_OPENGL_CORE_PROFILE);
        if (OperatingSystem.IsMacOS())
        {
            GLFW.WindowHint(GLFW.GLFW_OPENGL_FORWARD_COMPAT, 1);
        }

        scale = 1;
        var monitor = GLFW.GetPrimaryMonitor();
        if (!monitor.IsNull)
        {
            scale = ImGuiImplGLFW.GetContentScaleForMonitor(
                Unsafe.BitCast<GLFWmonitorPtr, Hexa.NET.ImGui.Backends.GLFW.GLFWmonitorPtr>(monitor));
            if (scale <= 0)
            {
                scale = 1;
            }
        }

        return GLFW.CreateWindow((int)(1100 * scale), (int)(700 * scale), "D3SK Commander", null, null);
    }

    private static unsafe bool StartBackends(GLFWwindowPtr window, ImGuiContextPtr gui)
    {
        ImGuiImplGLFW.SetCurrentContext(gui);
        var glfwWindow = Unsafe.BitCast<GLFWwindowPtr, Hexa.NET.ImGui.Backends.GLFW.GLFWwindowPtr>(window);
        if (!ImGuiImplGLFW.InitForOpenGL(glfwWindow, true))
        {
            return false;
        }

        ImGuiImplOpenGL3.SetCurrentContext(gui);
        if (!ImGuiImplOpenGL3.Init("#version 150"))
        {
            ImGuiImplGLFW.Shutdown();
            return false;
        }

        return true;
    }

    private void Loop(GLFWwindowPtr window, GL gl, ImGuiIOPtr io, MainWindow shell)
    {
        var dialogs = _services.GetRequiredService<IDialogSurface>();
        var tools = _services.GetRequiredService<IToolWindowHost>();
        while (GLFW.WindowShouldClose(window) == 0 && shell.IsVisible)
        {
            GLFW.PollEvents();
            UiThread.Drain();
            if (!shell.IsVisible || GLFW.WindowShouldClose(window) != 0)
            {
                break;
            }

            if (GLFW.GetWindowAttrib(window, GLFW.GLFW_ICONIFIED) != 0)
            {
                ImGuiImplGLFW.Sleep(10);
                continue;
            }

            GLFW.MakeContextCurrent(window);
            gl.ClearColor(0.12f, 0.12f, 0.14f, 1);
            gl.Clear(GLClearBufferMask.ColorBufferBit);
            ImGuiImplOpenGL3.NewFrame();
            ImGuiImplGLFW.NewFrame();
            ImGui.NewFrame();
            shell.Draw();
            dialogs.Draw();
            tools.Draw();
            if (!io.WantTextInput)
            {
                shell.HandleImGuiKeys();
            }

            ImGui.Render();
            GLFW.MakeContextCurrent(window);
            ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
            if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
            {
                ImGui.UpdatePlatformWindows();
                ImGui.RenderPlatformWindowsDefault();
            }

            GLFW.MakeContextCurrent(window);
            GLFW.SwapBuffers(window);
        }
    }

    private static void Destroy(GLFWwindowPtr window, ImGuiContextPtr gui, ImPlotContextPtr plot, bool backends)
    {
        if (backends)
        {
            ImGuiImplOpenGL3.Shutdown();
            ImGuiImplOpenGL3.SetCurrentContext(null);
            ImGuiImplGLFW.Shutdown();
            ImGuiImplGLFW.SetCurrentContext(null);
        }

        ImPlot.DestroyContext(plot);
        ImGui.SetCurrentContext(null);
        ImPlot.SetCurrentContext(null);
        ImGui.DestroyContext(gui);
        if (!window.IsNull)
        {
            GLFW.DestroyWindow(window);
        }

        GLFW.Terminate();
    }

    private static (int Width, int Height, byte[] Rgba) ReadPixels(GL gl, GLFWwindowPtr window)
    {
        var width = 0;
        var height = 0;
        GLFW.GetFramebufferSize(window, ref width, ref height);
        if (width < 1)
        {
            width = 1;
        }

        if (height < 1)
        {
            height = 1;
        }

        var pixels = new byte[width * height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            gl.PixelStorei(GLPixelStoreParameter.PackAlignment, 1);
            gl.ReadPixels(0, 0, width, height, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, pin.AddrOfPinnedObject());
        }
        finally
        {
            pin.Free();
        }

        FlipVertical(pixels, width, height);
        return (width, height, pixels);
    }

    private static void FlipVertical(byte[] pixels, int width, int height)
    {
        var stride = width * 4;
        var row = new byte[stride];
        for (var y = 0; y < height / 2; y++)
        {
            var top = y * stride;
            var bottom = (height - 1 - y) * stride;
            pixels.AsSpan(top, stride).CopyTo(row);
            pixels.AsSpan(bottom, stride).CopyTo(pixels.AsSpan(top, stride));
            row.CopyTo(pixels.AsSpan(bottom, stride));
        }
    }
}
