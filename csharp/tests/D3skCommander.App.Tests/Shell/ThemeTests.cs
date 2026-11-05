using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

/// <summary>The theme follows the platform: the app asks for <c>Default</c>, the OS decides.</summary>
public class ThemeTests
{
    [AvaloniaFact]
    public void TheAppFollowsThePlatformThemeVariant()
    {
        var app = Application.Current!;
        Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);

        var original = app.PlatformSettings!;
        var window = new Window();
        window.Show();
        try
        {
            SetPlatformVariant(app, original, PlatformThemeVariant.Dark);
            var dark = Resolved(window);
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);

            SetPlatformVariant(app, original, PlatformThemeVariant.Light);
            var light = Resolved(window);
            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);

            Assert.NotEqual(dark, light);
        }
        finally
        {
            window.Close();
            SetPlatformVariant(app, original, PlatformThemeVariant.Light);
        }
    }

    private static Color Resolved(Window window)
    {
        Assert.True(window.TryFindResource("SystemRegionColor", window.ActualThemeVariant, out var value));
        return Assert.IsType<Color>(value);
    }

    // The headless platform settings always answer Light and cannot be subclassed: register a proxy
    // that answers the wanted variant, then run the handler the application subscribes to the OS
    // change with (the locator and that handler are not public API).
    private static void SetPlatformVariant(Application app, IPlatformSettings original, PlatformThemeVariant variant)
    {
        var values = new PlatformColorValues { ThemeVariant = variant };
        var settings = DispatchProxy.Create<IPlatformSettings, SettingsProxy>();
        ((SettingsProxy)(object)settings).Setup(original, values);

        var locator = typeof(Visual).Assembly.GetType("Avalonia.AvaloniaLocator")!;
        var mutable = locator.GetProperty("CurrentMutable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var helper = mutable.GetType().GetMethod("Bind")!.MakeGenericMethod(typeof(IPlatformSettings)).Invoke(mutable, null)!;
        helper.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(IPlatformSettings)).Invoke(helper, new object[] { settings });

        var raise = typeof(Application).GetMethod("OnColorValuesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(raise);
        raise!.Invoke(app, new object?[] { settings, values });
    }

    /// <summary>Forwards to the headless settings, except the color values.</summary>
    public class SettingsProxy : DispatchProxy
    {
        private IPlatformSettings _inner = null!;
        private PlatformColorValues _values = null!;

        public void Setup(IPlatformSettings inner, PlatformColorValues values)
        {
            _inner = inner;
            _values = values;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name == nameof(IPlatformSettings.GetColorValues)
                ? _values
                : targetMethod.Invoke(_inner, args);
    }
}
