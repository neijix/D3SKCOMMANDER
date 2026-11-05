using System.Reflection;
using Xunit;

namespace D3skCommander.App.Tests.Architecture;

public class AppArchitectureTests
{
    [Fact]
    public void AppReferencesAvaloniaAndTheImGuiStack()
    {
        var app = Assembly.Load("D3skCommander");
        var names = app.GetReferencedAssemblies().Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Hexa.NET.ImGui", names);
        Assert.Contains("Hexa.NET.ImGui.Backends", names);
        Assert.Contains("Hexa.NET.ImGui.Backends.GLFW", names);
        Assert.Contains("Hexa.NET.GLFW", names);
        Assert.Contains("Hexa.NET.OpenGL3", names);
        Assert.Contains("Hexa.NET.ImPlot", names);
        Assert.Contains("Avalonia.Base", names);
    }
}
