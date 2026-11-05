using D3skCommander.Core.Timestamps;
using Xunit;

namespace D3skCommander.Core.Tests.Timestamps;

public class TimestampTests
{
    [Fact]
    public void ShiftMovesModifiedOnly()
    {
        using var dir = new TempFolder();
        var path = Make(dir.Path, "a.txt", 1_000_000);
        var results = FileTimes.ShiftTimes([path], 3600, modified: true, accessed: false);
        Assert.True(results[0].IsSuccess, results[0].Error?.Message);
        Assert.Equal(1_003_600, Mtime(path));
        Assert.Equal(1_000_000, Atime(path));
    }

    [Fact]
    public void ShiftNegativeAndAccessed()
    {
        using var dir = new TempFolder();
        var path = Make(dir.Path, "a.txt", 1_000_000);
        FileTimes.ShiftTimes([path], -60, modified: true, accessed: true);
        Assert.Equal(999_940, Mtime(path));
        Assert.Equal(999_940, Atime(path));
    }

    [Fact]
    public void ShiftMissingFileReportsError()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "nope");
        var results = FileTimes.ShiftTimes([path], 10, modified: true, accessed: true);
        Assert.False(results[0].IsSuccess);
    }

    [Fact]
    public void RedateStepsEachFile()
    {
        using var dir = new TempFolder();
        var files = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            files.Add(Make(dir.Path, $"f{i}", 5));
        }

        var results = FileTimes.ReDate(files, DateTimeOffset.FromUnixTimeSeconds(2_000_000), 90);
        foreach (var result in results)
        {
            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        Assert.Equal(2_000_000, Mtime(files[0]));
        Assert.Equal(2_000_090, Mtime(files[1]));
        Assert.Equal(2_000_180, Mtime(files[2]));
    }

    [Fact]
    public void RedateNegativeStep()
    {
        using var dir = new TempFolder();
        var files = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            files.Add(Make(dir.Path, $"f{i}", 5));
        }

        FileTimes.ReDate(files, DateTimeOffset.FromUnixTimeSeconds(2_000_000), -10);
        Assert.Equal(1_999_990, Mtime(files[1]));
    }

    [Fact]
    public void SetTimesSetsAndKeeps()
    {
        using var dir = new TempFolder();
        var path = Make(dir.Path, "a.txt", 1_000_000);
        FileTimes.SetTimes([path], DateTimeOffset.FromUnixTimeSeconds(3_000_000), null);
        Assert.Equal(3_000_000, Mtime(path));
        Assert.Equal(1_000_000, Atime(path));
        FileTimes.SetTimes([path], null, DateTimeOffset.FromUnixTimeSeconds(4_000_000));
        Assert.Equal(3_000_000, Mtime(path));
        Assert.Equal(4_000_000, Atime(path));
    }

    [Fact]
    public void ParseOffsetForms()
    {
        AssertOffset("+1h30m", 5400);
        AssertOffset("-2d", -172_800);
        AssertOffset("90s", 90);
        AssertOffset("90", 90);
        AssertOffset("1:30:00", 5400);
        AssertOffset("-0:05", -300);
        AssertOffset("1w", 604_800);
        AssertOffset(" 1d 2h ", 93_600);
        AssertOffset("+10M", 600);
    }

    [Fact]
    public void ParseOffsetRejectsGarbage()
    {
        foreach (var bad in new[] { "", "+", "abc", "1x", "h", "1:2:3:4", "1:x", "--5" })
        {
            Assert.False(FileTimes.ParseOffset(bad).IsSuccess, bad);
        }
    }

    private static void AssertOffset(string text, long expected)
    {
        var parsed = FileTimes.ParseOffset(text);
        Assert.True(parsed.IsSuccess, parsed.Error?.Message);
        Assert.Equal(expected, parsed.Seconds);
    }

    private static string Make(string directory, string name, long seconds)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "x");
        var time = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        File.SetLastAccessTimeUtc(path, time);
        File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    private static long Mtime(string path) => Unix(File.GetLastWriteTimeUtc(path));

    private static long Atime(string path) => Unix(File.GetLastAccessTimeUtc(path));

    private static long Unix(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("d3sk-time-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
