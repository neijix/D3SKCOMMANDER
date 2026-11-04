using System.Reflection;
using Xunit;

namespace D3skCommander.Core.Tests.Architecture;

public class CoreArchitectureTests
{
    [Fact]
    public void CoreReferencesNoGuiAssembly()
    {
        var core = Assembly.Load("D3skCommander.Core");
        var gui = core.GetReferencedAssemblies()
            .Where(a => IsGuiAssembly(a.Name))
            .ToList();
        Assert.Empty(gui);
    }

    private static bool IsGuiAssembly(string? name) =>
        name is not null
        && (name.StartsWith("Avalonia", StringComparison.Ordinal)
            || name.StartsWith("Hexa.NET", StringComparison.Ordinal)
            || name.Contains("ImGui", StringComparison.Ordinal)
            || name.Contains("GLFW", StringComparison.Ordinal));

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
