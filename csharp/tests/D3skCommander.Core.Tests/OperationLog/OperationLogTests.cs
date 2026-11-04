using System.Globalization;
using System.Text;
using D3skCommander.Core.OperationLog;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using OpLog = D3skCommander.Core.OperationLog.OperationLog;

namespace D3skCommander.Core.Tests.OperationLog;

/// <summary>P37: the operations log records each operation when it is turned on. A12: the log is injected.</summary>
public class OperationLogTests
{
    [Fact]
    public void RecordsOneLinePerOperationWhenTurnedOn()
    {
        using var dir = new ScratchFolder("d3sk-log-");
        var clock = FixedClock();
        var error = new StringWriter();
        IOperationLog log = new OpLog(clock, error) { Enabled = true };
        var src = Path.Combine(dir.Folder, "logged.txt");
        var dest = Path.Combine(dir.Folder, "out", "logged.txt");

        log.Record("COPY", src, dest, null);
        log.Record("COPY", Path.Combine(dir.Folder, "ghost.txt"), dest, "not found\there\r\n");

        var lines = log.ReadLines();
        Assert.Equal(2, lines.Count);

        var ok = lines[0].Split('\t');
        Assert.Equal(5, ok.Length);
        Assert.Equal(Stamp(clock), ok[0]);
        Assert.Equal(19, ok[0].Length);
        Assert.Equal("COPY", ok[1]);
        Assert.Equal(src, ok[2]);
        Assert.EndsWith("logged.txt", ok[3]);
        Assert.Equal("OK", ok[4]);

        var failed = lines[1].Split('\t');
        Assert.Equal(5, failed.Length);
        Assert.Equal("COPY", failed[1]);
        Assert.EndsWith("ghost.txt", failed[2]);
        Assert.Equal("ERROR not found here  ", failed[4]);
    }

    [Fact]
    public void WritesNothingWhenTurnedOff()
    {
        var error = new StringWriter();
        IOperationLog log = new OpLog(FixedClock(), error);
        log.Record("COPY", "a", "b", null);
        log.Record("DELETE", "a", null, "busy");

        Assert.Empty(log.ReadLines());
        Assert.Equal("", error.ToString());

        log.Enabled = true;
        log.Record("MOVE", "a", "b", null);
        log.Enabled = false;
        log.Record("MOVE", "c", "d", null);

        var lines = log.ReadLines();
        Assert.Single(lines);
        Assert.Equal("MOVE", lines[0].Split('\t')[1]);
        Assert.Equal("b", lines[0].Split('\t')[3]);
    }

    [Fact]
    public void AnEmptyDestinationIsAnEmptyField()
    {
        var clock = FixedClock();
        IOperationLog log = new OpLog(clock, new StringWriter()) { Enabled = true };
        log.Record("DELETE", "gone.txt", null, null);

        var fields = Assert.Single(log.ReadLines()).Split('\t');
        Assert.Equal(["DELETE", "gone.txt", "", "OK"], fields[1..]);
        Assert.Equal(Stamp(clock), fields[0]);
    }

    [Fact]
    public void EchoesToStderrUntilASettingsFolderIsGiven()
    {
        using var dir = new ScratchFolder("d3sk-log-");
        var captured = new StringWriter();
        var previous = Console.Error;
        Console.SetError(captured);
        try
        {
            IOperationLog log = new OpLog(FixedClock()) { Enabled = true };
            log.Record("COPY", "mem.txt", "out.txt", null);
            Assert.Contains("COPY", captured.ToString());
            Assert.Contains("mem.txt", Assert.Single(log.ReadLines()));

            log.UseSettingsFolder(dir.Folder);
            log.Record("MOVE", "disk.txt", "there.txt", null);

            var lines = log.ReadLines();
            Assert.Equal(2, lines.Count);
            Assert.Contains("mem.txt", lines[0]);
            Assert.Contains("disk.txt", lines[1]);
            Assert.DoesNotContain("disk.txt", captured.ToString());
            Assert.Equal(
                Path.Combine(dir.Folder, OpLog.FileName),
                Directory.GetFiles(dir.Folder).Single());
            var raw = File.ReadAllBytes(Path.Combine(dir.Folder, "ops.log"));
            Assert.Contains((byte)'\n', raw);
            Assert.DoesNotContain((byte)'\r', raw);
        }
        finally
        {
            Console.SetError(previous);
        }
    }

    [Fact]
    public void AppendsToAnExistingOpsLog()
    {
        using var dir = new ScratchFolder("d3sk-log-");
        var path = Path.Combine(dir.Folder, "ops.log");
        File.WriteAllText(path, "already there\n");
        IOperationLog log = new OpLog(FixedClock(), new StringWriter()) { Enabled = true };

        log.UseSettingsFolder(dir.Folder);
        log.Record("COPY", "a", "b", null);

        var lines = log.ReadLines();
        Assert.Equal("already there", lines[0]);
        Assert.Equal("COPY", lines[1].Split('\t')[1]);
    }

    [Fact]
    public void LoggingNeverThrows()
    {
        using var dir = new ScratchFolder("d3sk-log-");
        IOperationLog log = new OpLog(FixedClock(), new ThrowingWriter()) { Enabled = true };
        var memory = Record.Exception(() => log.Record("COPY", "a", "b", null));
        Assert.Null(memory);
        Assert.Contains("COPY", Assert.Single(log.ReadLines()));

        log.UseSettingsFolder(dir.Folder);
        var ops = Path.Combine(dir.Folder, OpLog.FileName);
        File.Delete(ops);
        Directory.CreateDirectory(ops);
        var disk = Record.Exception(() => log.Record("COPY", "a", "b", null));
        Assert.Null(disk);
    }

    private static FakeTimeProvider FixedClock()
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero));
        return clock;
    }

    private static string Stamp(TimeProvider clock) =>
        clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private sealed class ThrowingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value) => throw new IOException("stderr closed");
    }

    private sealed class ScratchFolder : IDisposable
    {
        public ScratchFolder(string prefix)
        {
            Folder = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
        }

        public string Folder { get; }

        public void Dispose()
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
    }
}
