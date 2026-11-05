using System.Reflection;
using Xunit;

namespace D3skCommander.App.Tests.Architecture;

public class AppArchitectureTests
{
    [Fact]
    public void AppReferencesAvaloniaAndNoHexaAssembly()
    {
        var app = Assembly.Load("D3skCommander");
        var names = app.GetReferencedAssemblies().Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Avalonia.Base", names);
        Assert.DoesNotContain(names, n => n.StartsWith("Hexa.", StringComparison.Ordinal));
    }
}
