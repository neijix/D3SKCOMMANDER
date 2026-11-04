using System.Diagnostics;
using D3skCommander.Core.Workspace;
using Xunit;

namespace D3skCommander.Core.Tests.Workspace;

/// <summary>A12: the temp workspace is injected, removed on exit, and swept at the next start.</summary>
public class WorkspaceTests
{
    [Fact]
    public void HandsOutASessionFolderAndRemovesItOnExit()
    {
        using var root = new ScratchFolder();
        var firstPath = "";
        using (ITempWorkspace first = new TempWorkspace(root.Folder))
        {
            firstPath = first.SessionFolder;
            Assert.True(Directory.Exists(firstPath));
            Assert.StartsWith(root.Folder, firstPath);
            Assert.Matches($"^{Environment.ProcessId}-[0-9a-f]{{32}}$", Path.GetFileName(firstPath));
            File.WriteAllText(Path.Combine(firstPath, "extract.txt"), "x");
            var nested = Path.Combine(firstPath, "sub");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "a.txt"), "y");

            using (ITempWorkspace second = new TempWorkspace(root.Folder))
            {
                Assert.NotEqual(firstPath, second.SessionFolder);
                Assert.True(Directory.Exists(second.SessionFolder));
                Assert.Equal(2, Directory.GetDirectories(root.Folder).Length);
            }

            Assert.True(Directory.Exists(firstPath));
            Assert.Single(Directory.GetDirectories(root.Folder));
        }

        Assert.False(Directory.Exists(firstPath));
        Assert.Empty(Directory.GetDirectories(root.Folder));
        Assert.True(Directory.Exists(root.Folder));
    }

    [Fact]
    public void NextStartRemovesLeftoversFromDeadProcesses()
    {
        using var root = new ScratchFolder();
        var dead = DeadPid();
        var deadSession = Path.Combine(root.Folder, $"{dead}-{Guid.NewGuid():N}");
        var deadV01 = Path.Combine(root.Folder, dead.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(deadSession);
        File.WriteAllText(Path.Combine(deadSession, "stale.txt"), "old");
        Directory.CreateDirectory(deadV01);
        File.WriteAllText(Path.Combine(deadV01, "stale.txt"), "old");
        var unrelated = Path.Combine(root.Folder, "keep-me");
        Directory.CreateDirectory(unrelated);
        var notASession = Path.Combine(root.Folder, dead + "-not-a-session-suffix");
        Directory.CreateDirectory(notASession);

        using (ITempWorkspace workspace = new TempWorkspace(root.Folder))
        {
            Assert.False(Directory.Exists(deadSession));
            Assert.False(Directory.Exists(deadV01));
            Assert.True(Directory.Exists(unrelated));
            Assert.True(Directory.Exists(notASession));
            Assert.True(Directory.Exists(workspace.SessionFolder));
        }
    }

    [Fact]
    public void KeepsAFolderWhoseProcessIsStillRunning()
    {
        using var root = new ScratchFolder();
        var liveSession = Path.Combine(root.Folder, $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        var liveV01 = Path.Combine(root.Folder, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(liveSession);
        File.WriteAllText(Path.Combine(liveSession, "busy.txt"), "x");
        Directory.CreateDirectory(liveV01);

        using (ITempWorkspace workspace = new TempWorkspace(root.Folder))
        {
            Assert.True(Directory.Exists(liveSession));
            Assert.Equal("x", File.ReadAllText(Path.Combine(liveSession, "busy.txt")));
            Assert.True(Directory.Exists(liveV01));
            Assert.NotEqual(liveSession, workspace.SessionFolder);
        }

        Assert.True(Directory.Exists(liveSession));
        Assert.True(Directory.Exists(liveV01));
    }

    [Fact]
    public void TheDefaultRootIsTheProcessTempFolder()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(), "d3skcommander"), TempWorkspace.DefaultRoot);
        Assert.Equal("d3skcommander", TempWorkspace.RootFolderName);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        using var root = new ScratchFolder();
        ITempWorkspace workspace = new TempWorkspace(root.Folder);
        var path = workspace.SessionFolder;
        workspace.Dispose();
        var thrown = Record.Exception(() => workspace.Dispose());
        Assert.Null(thrown);
        Assert.False(Directory.Exists(path));
    }

    private static int DeadPid()
    {
        for (var pid = 2_000_000_000; pid < 2_000_001_000; pid++)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return pid;
            }
        }

        throw new InvalidOperationException("no unused process id");
    }

    private sealed class ScratchFolder : IDisposable
    {
        public ScratchFolder()
        {
            Folder = Path.Combine(Path.GetTempPath(), "d3sk-ws-" + Guid.NewGuid().ToString("N"));
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
