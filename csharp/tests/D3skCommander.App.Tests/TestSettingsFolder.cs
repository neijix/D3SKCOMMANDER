using System.Runtime.CompilerServices;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.App.Tests;

/// <summary>
/// Every App test (and every program a test starts) uses a throw-away settings folder,
/// never the user's: <c>D3SK_CONFIG_DIR</c> is set before the first test runs.
/// </summary>
internal static class TestSettingsFolder
{
    public static string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "d3sk-app-tests-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

#pragma warning disable CA2255 // A test assembly sets the process-wide settings folder before any test.
    [ModuleInitializer]
    internal static void Use()
    {
        Directory.CreateDirectory(Path);
        Environment.SetEnvironmentVariable(SettingsPlaces.ConfigDirVariable, Path);
    }
#pragma warning restore CA2255
}
