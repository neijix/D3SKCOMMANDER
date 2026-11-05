using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using D3skCommander.App.Smoke;
using D3skCommander.Core.Common;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Smoke;

/// <summary>
/// Starts the real program with <c>D3SK_RENDERING=software</c> and the screenshot hook: it must open
/// its window, draw it on the CPU, save the shot and quit cleanly. On Linux it runs under Xvfb, an
/// X server with no GPU at all; on Windows and macOS only in CI, where a window on the desktop
/// bothers nobody.
/// </summary>
public class SoftwareRenderingTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    [AvaloniaFact]
    public async Task TheProgramDrawsItsWindowWithoutAGpu()
    {
        var start = StartInfo();
        var folder = Directory.CreateTempSubdirectory("d3sk-software-");
        try
        {
            var shot = Path.Combine(folder.FullName, "shot.png");
            start.Environment[Rendering.Variable] = "software";
            start.Environment[SmokeModule.ScreenshotVariable] = shot;
            start.Environment[SmokeModule.CommandVariable] = null;
            start.Environment[SettingsPlaces.ConfigDirVariable] = Path.Combine(folder.FullName, "config");

            var (exit, error) = await RunAsync(start);

            Assert.True(exit == 0, $"exit code {exit}; stderr:\n{error}");
            Assert.Contains("screenshot saved: " + shot, error, StringComparison.Ordinal);
            AssertDrawn(shot);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // The program sits next to the tests (the App project is referenced). Linux wraps it in xvfb-run.
    private static ProcessStartInfo StartInfo()
    {
        var program = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "D3skCommander.exe" : "D3skCommander");
        Assert.True(File.Exists(program), "The program is missing from the test output: " + program);
        if (!OperatingSystem.IsLinux())
        {
            Assert.SkipUnless(
                string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
                "opens a real window: only in CI on Windows and macOS");
            return Redirected(program);
        }

        var xvfb = FindOnPath("xvfb-run");
        Assert.SkipWhen(xvfb is null, "xvfb-run is not installed (package xvfb)");
        var start = Redirected(xvfb!);
        start.ArgumentList.Add("-a");
        start.ArgumentList.Add(program);
        return start;
    }

    private static ProcessStartInfo Redirected(string file) => new(file)
    {
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        WorkingDirectory = AppContext.BaseDirectory,
    };

    private static async Task<(int Exit, string Error)> RunAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("no process: " + start.FileName);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(Limit);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"the program did not quit within {Limit.TotalSeconds} s");
        }

        await output;
        return (process.ExitCode, await error);
    }

    // A real window, not an empty or one-colour frame: the shell paints menus, panels and the key bar.
    private static void AssertDrawn(string path)
    {
        Assert.True(File.Exists(path), "no screenshot at " + path);
        using var bitmap = new Bitmap(path);
        Assert.True(bitmap.PixelSize.Width > 200 && bitmap.PixelSize.Height > 200, "shot is " + bitmap.PixelSize);
        var stride = bitmap.PixelSize.Width * 4;
        var pixels = new byte[stride * bitmap.PixelSize.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var colours = new HashSet<int>();
        for (var i = 0; i < pixels.Length && colours.Count < 16; i += 4)
        {
            colours.Add(BitConverter.ToInt32(pixels, i));
        }

        Assert.True(colours.Count >= 16, $"the shot holds only {colours.Count} colours: nothing was drawn");
    }

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
}
