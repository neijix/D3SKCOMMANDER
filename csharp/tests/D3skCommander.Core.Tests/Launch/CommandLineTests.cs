using D3skCommander.Core.Common;
using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.Core.Tests.Launch;

/// <summary>v0.1 command-line <c>cd</c> rules (P9). Any other line runs in the shell in the active folder.</summary>
public class CommandLineTests
{
    [Fact]
    public void CdWithNoArgumentGoesHome()
    {
        using var tree = new Tree();
        var decision = CommandLine.Interpret("cd", tree.Active, tree.Home);
        Assert.Equal(CommandLineKind.Navigate, decision.Kind);
        Assert.Equal(Full(tree.Home), decision.Path);
    }

    [Fact]
    public void CdTildeGoesHome()
    {
        using var tree = new Tree();
        foreach (var line in new[] { "cd ~", "cd \"~\"" })
        {
            var decision = CommandLine.Interpret(line, tree.Active, tree.Home);
            Assert.Equal(CommandLineKind.Navigate, decision.Kind);
            Assert.Equal(Full(tree.Home), decision.Path);
        }
    }

    [Fact]
    public void CdTildeSlashJoinsHome()
    {
        using var tree = new Tree();
        var nested = Directory.CreateDirectory(Path.Combine(tree.Home, "x", "y")).FullName;
        foreach (var line in new[] { "cd ~/x", "cd \"~/x\"", "cd ~/x/y" })
        {
            var decision = CommandLine.Interpret(line, tree.Active, tree.Home);
            Assert.Equal(CommandLineKind.Navigate, decision.Kind);
        }

        Assert.Equal(Full(Path.Combine(tree.Home, "x")), CommandLine.Interpret("cd ~/x", tree.Active, tree.Home).Path);
        Assert.Equal(nested, CommandLine.Interpret("cd ~/x/y", tree.Active, tree.Home).Path);
    }

    [Fact]
    public void CdRelativeJoinsTheActiveFolder()
    {
        using var tree = new Tree();
        var decision = CommandLine.Interpret("  cd sub  ", tree.Active, tree.Home);
        Assert.Equal(CommandLineKind.Navigate, decision.Kind);
        Assert.Equal(Full(tree.Sub), decision.Path);

        var parent = CommandLine.Interpret("cd ..", tree.Active, tree.Home);
        Assert.Equal(CommandLineKind.Navigate, parent.Kind);
        Assert.Equal(tree.Root.FullName, parent.Path);

        var absolute = CommandLine.Interpret("cd \"" + tree.Home + "\"", tree.Active, tree.Home);
        Assert.Equal(Full(tree.Home), absolute.Path);
    }

    [Fact]
    public void CdQuotedPathKeepsSpaces()
    {
        using var tree = new Tree();
        var decision = CommandLine.Interpret("cd \"my folder\"", tree.Active, tree.Home);
        Assert.Equal(CommandLineKind.Navigate, decision.Kind);
        Assert.Equal(Full(tree.Spaced), decision.Path);
    }

    [Fact]
    public void CdMissingFolderIsAnError()
    {
        using var tree = new Tree();
        var missing = Path.Combine(tree.Active, "no-such");
        var decision = CommandLine.Interpret("cd no-such", tree.Active, tree.Home);
        Assert.Equal(CommandLineKind.Error, decision.Kind);
        Assert.Equal(ErrorKind.NotFound, decision.Error!.Kind);
        Assert.Equal("Folder: " + missing, decision.Error.Message);
        Assert.Equal(missing, decision.Error.Path);

        var underHome = Path.Combine(tree.Home, "missing");
        var tilde = CommandLine.Interpret("cd ~/missing", tree.Active, tree.Home);
        Assert.Equal("Folder: " + underHome, tilde.Error!.Message);

        var file = CommandLine.Interpret("cd note.txt", tree.Active, tree.Home);
        Assert.Equal(CommandLineKind.Error, file.Kind);
        Assert.Equal("Folder: " + Full(tree.Note), file.Error!.Message);
    }

    [Fact]
    public void OtherLinesRunInTheShellInTheActiveFolder()
    {
        using var tree = new Tree();
        var decision = CommandLine.Interpret("echo hi", tree.Active, tree.Home);
        Assert.Equal(CommandLineKind.Shell, decision.Kind);
        Assert.Equal("echo hi", decision.Line);
        Assert.Equal(tree.Active, decision.WorkingDirectory);

        var runner = new RecordingRunner();
        var result = Launcher.RunInShell(decision.Line, decision.WorkingDirectory!, runner);
        Assert.True(result.IsSuccess);
        var shell = Assert.IsType<ProcessRequest.Shell>(Assert.Single(runner.Requests));
        Assert.Equal("echo hi", shell.CommandLine);
        Assert.Equal(tree.Active, shell.WorkingDirectory);
    }

    [Fact]
    public void EmptyLineDoesNothing()
    {
        var decision = CommandLine.Interpret("   ", "/work", "/home");
        Assert.Equal(CommandLineKind.None, decision.Kind);
    }

    [Fact]
    public void ShellUsesHomeWhenThePanelHasNoFolder()
    {
        var decision = CommandLine.Interpret("ls", null, "/home/me");
        Assert.Equal(CommandLineKind.Shell, decision.Kind);
        Assert.Equal("ls", decision.Line);
        Assert.Equal("/home/me", decision.WorkingDirectory);
    }

    private static string Full(string path) => new DirectoryInfo(path).FullName;

    private sealed class Tree : IDisposable
    {
        public Tree()
        {
            Root = Directory.CreateTempSubdirectory("d3sk-cd-");
            Home = Directory.CreateDirectory(Path.Combine(Root.FullName, "home")).FullName;
            Active = Directory.CreateDirectory(Path.Combine(Root.FullName, "active")).FullName;
            Sub = Directory.CreateDirectory(Path.Combine(Active, "sub")).FullName;
            Spaced = Directory.CreateDirectory(Path.Combine(Active, "my folder")).FullName;
            Note = Path.Combine(Active, "note.txt");
            File.WriteAllText(Note, "x");
        }

        public DirectoryInfo Root { get; }

        public string Home { get; }

        public string Active { get; }

        public string Sub { get; }

        public string Spaced { get; }

        public string Note { get; }

        public void Dispose() => Root.Delete(recursive: true);
    }
}
