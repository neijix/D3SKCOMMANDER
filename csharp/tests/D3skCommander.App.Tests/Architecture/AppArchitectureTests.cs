using System.Reflection;
using System.Xml.Linq;
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

    /// <summary>Compiled bindings everywhere: every AXAML file names the type its bindings read on its root.</summary>
    [Fact]
    public void EveryAxamlFileDeclaresItsDataType()
    {
        var source = Path.Combine(SolutionFolder(), "src", "D3skCommander.App");
        var files = Directory.GetFiles(source, "*.axaml", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var missing = files
            .Where(file => XDocument.Load(file).Root?.Attribute(x + "DataType") is null)
            .Select(file => Path.GetRelativePath(source, file))
            .ToList();
        Assert.Empty(missing);
    }

    private static string SolutionFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "D3skCommander.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("D3skCommander.sln is not above the test output.");
    }
}
