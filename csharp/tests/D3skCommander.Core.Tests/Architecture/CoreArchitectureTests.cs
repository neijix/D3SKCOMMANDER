using System.Reflection;
using Xunit;

namespace D3skCommander.Core.Tests.Architecture;

public class CoreArchitectureTests
{
    [Fact]
    public void CoreReferencesNoAvaloniaAssembly()
    {
        var core = Assembly.Load("D3skCommander.Core");
        var avalonia = core.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("Avalonia", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(avalonia);
    }

    [Fact]
    public void CoreCarriesVersionAndRepositoryAsMetadata()
    {
        var core = Assembly.Load("D3skCommander.Core");
        var version = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var url = core.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryUrl").Value;
        Assert.Equal("0.2.0", version);
        Assert.Equal("https://github.com/neijix/D3SKCOMMANDER", url);
    }

    [Fact]
    public void FixturesAreCopiedToTheOutput()
    {
        Assert.True(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "fixtures", "gio")));
    }
}
