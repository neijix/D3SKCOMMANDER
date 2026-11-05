using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using D3skCommander.App;
using D3skCommander.App.Shell;
using D3skCommander.App.Smoke;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Smoke;

public class ScreenshotHookTests
{
    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);

    [AvaloniaFact]
    public void HookIsActiveOnlyWhenTheScreenshotVariableIsSet()
    {
        using (var idle = Hook.Resolve((SmokeModule.ScreenshotVariable, null), (SmokeModule.CommandVariable, "Keys")))
        {
            Assert.False(idle.Smoke.IsActive);
        }

        using (var armed = Hook.Resolve((SmokeModule.ScreenshotVariable, Path.GetTempFileName())))
        {
            Assert.True(armed.Smoke.IsActive);
            Assert.Contains(armed.Provider.GetServices<IShellModule>(), module => ReferenceEquals(module, armed.Smoke));
        }
    }

    [AvaloniaFact]
    public void ScreenshotSavesAPngAfterTwoSecondsAndQuits()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(2000), SmokeModule.ShotDelay);
        var path = NewPngPath();
        var clock = new FakeTimeProvider();
        var error = new StringWriter();
        using var hook = Hook.Show(clock, error, (SmokeModule.ScreenshotVariable, path));
        var window = hook.RequireWindow();
        Assert.True(hook.Smoke.IsActive);
        clock.Advance(SmokeModule.ShotDelay - OneMillisecond);
        Assert.False(File.Exists(path));
        Assert.True(window.IsVisible);
        var width = Pixels(window.Bounds.Width);
        var height = Pixels(window.Bounds.Height);

        clock.Advance(OneMillisecond);
        Assert.False(window.IsVisible);
        AssertPng(path, width, height);
        Assert.Contains("screenshot saved: " + path, error.ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void NamedCommandRunsAfterEightHundredMilliseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(800), SmokeModule.CommandDelay);
        var path = NewPngPath();
        var clock = new FakeTimeProvider();
        var error = new StringWriter();
        var probe = new CommandProbe();
        using var hook = Hook.Show(
            clock,
            error,
            probe,
            (SmokeModule.ScreenshotVariable, path),
            (SmokeModule.CommandVariable, "kEyS"));
        var window = hook.RequireWindow();
        clock.Advance(SmokeModule.CommandDelay - OneMillisecond);
        Assert.Equal(0, probe.Runs);
        Assert.False(File.Exists(path));

        clock.Advance(OneMillisecond);
        Assert.Equal(1, probe.Runs);
        Assert.True(window.IsVisible);
        Assert.False(File.Exists(path));

        var width = Pixels(window.Bounds.Width);
        var height = Pixels(window.Bounds.Height);
        clock.Advance(SmokeModule.ShotDelay - SmokeModule.CommandDelay);
        Assert.Equal(1, probe.Runs);
        Assert.False(window.IsVisible);
        AssertPng(path, width, height);
    }

    [AvaloniaFact]
    public void UnknownCommandNameIsReportedOnStderr()
    {
        var path = NewPngPath();
        var clock = new FakeTimeProvider();
        var error = new StringWriter();
        var probe = new CommandProbe();
        using var hook = Hook.Show(
            clock,
            error,
            probe,
            (SmokeModule.ScreenshotVariable, path),
            (SmokeModule.CommandVariable, "Nope"));
        var window = hook.RequireWindow();
        clock.Advance(SmokeModule.CommandDelay);
        Assert.Equal(0, probe.Runs);
        Assert.Contains("unknown command: Nope", error.ToString(), StringComparison.Ordinal);
        Assert.True(window.IsVisible);
        Assert.DoesNotContain("screenshot saved:", error.ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheScreenshotIsNotOneSolidColour()
    {
        var path = NewPngPath();
        var clock = new FakeTimeProvider();
        using var hook = Hook.Show(clock, new StringWriter(), (SmokeModule.ScreenshotVariable, path));
        hook.RequireWindow();
        clock.Advance(SmokeModule.ShotDelay);
        AssertPng(path, 1100, 700);
        Assert.True(Colours(path) > 3, "the PNG is one solid colour");
    }

    [AvaloniaFact]
    public void AfterACommandThatOpensAWindowTheShotIsThatWindow()
    {
        var path = NewPngPath();
        var clock = new FakeTimeProvider();
        var probe = new CommandProbe();
        using var hook = Hook.Show(
            clock,
            new StringWriter(),
            probe,
            (SmokeModule.ScreenshotVariable, path),
            (SmokeModule.CommandVariable, "Keys"));
        var main = hook.RequireWindow();
        probe.Opens = () =>
        {
            var dialog = new Window { Width = 420, Height = 260, Title = "dialog" };
            dialog.Show(main);
            return dialog;
        };
        clock.Advance(SmokeModule.ShotDelay);
        AssertPng(path, 420, 260);
        Assert.False(main.IsVisible);
    }

    [AvaloniaFact]
    public void AStalePngFromAnEarlierRunIsNeverKept()
    {
        var path = NewPngPath();
        byte[] stale = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1];
        File.WriteAllBytes(path, stale);
        var clock = new FakeTimeProvider();
        using var hook = Hook.Show(clock, new StringWriter(), (SmokeModule.ScreenshotVariable, path));
        var window = hook.RequireWindow();
        var width = Pixels(window.Bounds.Width);
        var height = Pixels(window.Bounds.Height);

        clock.Advance(SmokeModule.ShotDelay);

        AssertPng(path, width, height);
    }

    private static int Colours(string path)
    {
        using var bitmap = new Bitmap(path);
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), pin.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            pin.Free();
        }

        var seen = new HashSet<int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            seen.Add(BitConverter.ToInt32(pixels, i));
        }

        return seen.Count;
    }

    private static string NewPngPath() =>
        Path.Combine(Path.GetTempPath(), "d3sk-shot-" + Guid.NewGuid().ToString("N") + ".png");

    private static int Pixels(double dips) => (int)Math.Ceiling(dips);

    private static void AssertPng(string path, int width, int height)
    {
        var png = File.ReadAllBytes(path);
        Assert.True(png.Length > 24);
        Assert.Equal(137, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
        Assert.Equal(width, Be(png, 16));
        Assert.Equal(height, Be(png, 20));
    }

    private static int Be(byte[] png, int offset) =>
        (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];

    private sealed class CommandProbe : IShellModule
    {
        public int Runs { get; private set; }

        public Func<Window>? Opens { get; set; }

        public void Contribute(IShellContributions shell) => shell.Handle(CommandId.Keys, () =>
        {
            Runs++;
            Opens?.Invoke();
        });
    }

    private sealed class Hook : IDisposable
    {
        private static readonly object Gate = new();

        private readonly string? _png;

        private Hook(ServiceProvider provider, MainWindow? window, string? png)
        {
            Provider = provider;
            Window = window;
            _png = png;
            Smoke = provider.GetRequiredService<SmokeModule>();
        }

        public ServiceProvider Provider { get; }

        public MainWindow? Window { get; }

        public MainWindow RequireWindow()
        {
            var window = Window;
            Assert.NotNull(window);
            return window;
        }

        public SmokeModule Smoke { get; }

        public static Hook Resolve(params (string Name, string? Value)[] variables) =>
            Open(null, new StringWriter(), null, show: false, variables);

        public static Hook Show(FakeTimeProvider clock, StringWriter error, params (string Name, string? Value)[] variables) =>
            Open(clock, error, null, show: true, variables);

        public static Hook Show(
            FakeTimeProvider clock,
            StringWriter error,
            CommandProbe probe,
            params (string Name, string? Value)[] variables) =>
            Open(clock, error, probe, show: true, variables);

        public void Dispose()
        {
            var window = Window;
            if (window is { IsVisible: true })
            {
                window.Close();
            }

            Provider.Dispose();
            if (_png is not null && File.Exists(_png))
            {
                File.Delete(_png);
            }
        }

        private static Hook Open(
            FakeTimeProvider? clock,
            StringWriter error,
            CommandProbe? probe,
            bool show,
            (string Name, string? Value)[] variables)
        {
            var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            var provider = Program.BuildProvider(localizer, services =>
            {
                if (clock is not null)
                {
                    services.AddSingleton<TimeProvider>(clock);
                }

                services.AddSingleton<TextWriter>(error);
                if (probe is not null)
                {
                    services.AddSingleton<IShellModule>(probe);
                }
            });
            MainWindow? window = null;
            lock (Gate)
            {
                using (new EnvScope(variables))
                {
                    if (show)
                    {
                        window = provider.GetRequiredService<MainWindow>();
                    }
                    else
                    {
                        _ = provider.GetRequiredService<SmokeModule>();
                    }
                }
            }

            if (show)
            {
                window!.Show();
            }

            string? png = null;
            foreach (var (name, value) in variables)
            {
                if (name == SmokeModule.ScreenshotVariable)
                {
                    png = value;
                }
            }

            return new Hook(provider, window, png);
        }
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly (string Name, string? Previous)[] _saved;

        public EnvScope(params (string Name, string? Value)[] pairs)
        {
            _saved = new (string Name, string? Previous)[pairs.Length];
            for (var i = 0; i < pairs.Length; i++)
            {
                _saved[i] = (pairs[i].Name, Environment.GetEnvironmentVariable(pairs[i].Name));
                Environment.SetEnvironmentVariable(pairs[i].Name, pairs[i].Value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _saved)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}
