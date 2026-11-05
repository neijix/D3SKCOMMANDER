using D3skCommander.App.Startup;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Startup;

public sealed class StartupWiringTests
{
    [Fact]
    public void WithTheConfigVariableTheStoresAndTheLogUseThatFolder()
    {
        var folder = Environment.GetEnvironmentVariable(SettingsPlaces.ConfigDirVariable);
        Assert.False(string.IsNullOrEmpty(folder));
        using var services = TestComposition.Build();
        var settings = services.GetRequiredService<AppSettings>();
        Assert.Equal(folder, settings.Directory);
        Assert.Equal(SettingsSource.Env, settings.Folder.Source);

        settings.Hotlist.Save(new Hotlist { Items = [new HotlistItem("tmp", folder!)] });
        settings.Flush();
        Assert.True(File.Exists(Path.Combine(folder!, SettingsFileNames.Hotlist)));

        var log = services.GetRequiredService<IOperationLog>();
        log.Enabled = true;
        log.Record("copy", "/a", "/b", null);
        Assert.True(File.Exists(Path.Combine(folder!, SettingsFileNames.OperationsLog)));
        log.Enabled = false;
    }
}
