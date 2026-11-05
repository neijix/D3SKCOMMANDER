using D3skCommander.Core.Tools;
using Xunit;

namespace D3skCommander.Core.Tests.Tools;

public class ToolInputTests
{
    [Fact]
    public void DatesParseInThreeForms()
    {
        Assert.True(ToolInput.ParseDate("2024-02-29").IsSuccess);
        Assert.True(ToolInput.ParseDate("2024-02-29 13:45").IsSuccess);
        Assert.True(ToolInput.ParseDate("2024-02-29 13:45:10").IsSuccess);
        Assert.False(ToolInput.ParseDate("29/02/2024").IsSuccess);
        var later = ToolInput.ParseDate("2024-01-01 00:00:10");
        var midnight = ToolInput.ParseDate("2024-01-01");
        Assert.True(later.IsSuccess, later.Error?.Message);
        Assert.True(midnight.IsSuccess, midnight.Error?.Message);
        Assert.Equal(TimeSpan.FromSeconds(10), later.Time - midnight.Time);
    }

    [Fact]
    public void FoldersExpandToTheirFiles()
    {
        using var dir = new TempFolder();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "sub", "a.txt"), "a");
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "b");
        var files = ToolInput.ExpandFiles([Path.Combine(dir.Path, "sub"), Path.Combine(dir.Path, "b.txt")]);
        files.Sort(StringComparer.Ordinal);
        Assert.Equal(2, files.Count);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("d3sk-tools-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
