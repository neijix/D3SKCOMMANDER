using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using D3skCommander.Core.Toolbar;
using Xunit;

namespace D3skCommander.Core.Tests.Toolbar;

public class ToolbarTests
{
    [Fact]
    public void TheDefaultsAreTheDocumentedSetWithKnownIcons()
    {
        var items = ToolbarDefaults.Items();

        var commands = items.Select(item => item.Kind == ToolbarItemKind.Separator ? "|" : item.Command.ToString()!);
        Assert.Equal(
            ["Refresh", "|", "Copy", "Move", "MkDir", "Delete", "|", "Search", "QuickFind", "DiskUsage", "|", "CompareFiles", "Sync", "Settings"],
            commands);
        Assert.All(items.Where(item => item.Kind != ToolbarItemKind.Separator), item => Assert.True(ToolbarIcons.IsKnown(item.Icon)));
        Assert.Equal(items, ToolbarItems.Clean(items));
        Assert.True(ToolbarIcons.Names.Count >= 20);
        Assert.Equal(ToolbarIcons.Names.Count, ToolbarIcons.Names.Distinct().Count());
    }

    [Fact]
    public void AnIconDefaultsFromTheCommandOrTheKind()
    {
        Assert.Equal("copy", ToolbarItem.ForCommand(CommandId.Copy).EffectiveIcon());
        Assert.Equal("star", ToolbarItem.ForCommand(CommandId.Hotlist, "nope").EffectiveIcon());
        Assert.Equal("move", ToolbarItem.ForCommand(CommandId.Copy, "move").EffectiveIcon());
        Assert.Equal(ToolbarIcons.Generic, ToolbarItem.ForCommand(CommandId.About).EffectiveIcon());
        Assert.Equal(ToolbarIcons.UserCommand, ToolbarItem.ForUserCommand("x").EffectiveIcon());
        Assert.Equal(ToolbarIcons.Program, ToolbarItem.ForProgram("x").EffectiveIcon());
    }

    [Fact]
    public void ThePreferencesRoundTripTheToolbarThroughTheFile()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-toolbar-").FullName;
        try
        {
            var made = new Preferences
            {
                ShowToolbar = false,
                Toolbar =
                [
                    ToolbarItem.ForCommand(CommandId.Delete, "delete", "Remove it"),
                    ToolbarItem.Separator,
                    ToolbarItem.ForUserCommand("Open", "bolt"),
                    ToolbarItem.ForProgram("/opt/my tool/run", "--in \"%s\" -v", "/tmp", "program", "Run"),
                ],
            };
            using (var store = new PreferencesStore(folder))
            {
                store.Save(made);
            }

            using var reread = new PreferencesStore(folder);
            Assert.False(reread.Current.ShowToolbar);
            Assert.Equal(made.Toolbar, reread.Current.Toolbar);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ANewFileHasTheToolbarShownWithTheDefaults()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-toolbar-").FullName;
        try
        {
            using var store = new PreferencesStore(folder);
            Assert.True(store.Current.ShowToolbar);
            Assert.Equal(ToolbarDefaults.Items(), store.Current.Toolbar);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void AMalformedOrUnknownEntryIsDroppedAndTheOthersStay()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-toolbar-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, SettingsFileNames.Preferences), """
                {
                  "schema": 1,
                  "toolbar": [
                    { "kind": "Command", "command": "Copy" },
                    { "kind": "Command", "command": "NoSuchCommand" },
                    { "kind": "Command", "command": 7 },
                    { "kind": "Command" },
                    { "kind": "Teleport", "command": "Move" },
                    { "kind": 3 },
                    { "kind": "UserCommand", "userCommand": "" },
                    { "kind": "Program", "program": "   " },
                    "text",
                    null,
                    42,
                    { "kind": "separator" },
                    { "kind": "program", "program": "tool", "icon": "no_such_icon", "arguments": 5 }
                  ]
                }
                """);
            using var store = new PreferencesStore(folder);

            Assert.Equal(
                [ToolbarItem.ForCommand(CommandId.Copy), ToolbarItem.Separator, ToolbarItem.ForProgram("tool")],
                store.Current.Toolbar);
            Assert.Null(store.TakeLoadError());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ACommandButtonResolvesToItsCommand()
    {
        var action = ToolbarActions.Resolve(ToolbarItem.ForCommand(CommandId.Sync), Context());

        Assert.Equal(CommandId.Sync, action.Command);
        Assert.Empty(action.Processes);
    }

    [Fact]
    public void AProgramButtonExpandsTheSelectionAndKeepsAPathWithSpaces()
    {
        var item = ToolbarItem.ForProgram("/opt/my tool/run it", "--in \"%s\" -v \"two words\"");
        var files = new[] { "/data/a b.txt", "/data/c.txt" };

        var action = ToolbarActions.Resolve(item, Context(files));

        var start = Assert.IsType<ProcessRequest.Argv>(Assert.Single(action.Processes));
        Assert.Null(action.Command);
        Assert.Equal("/opt/my tool/run it", start.Program);
        Assert.Equal(["--in", "/data/a b.txt", "/data/c.txt", "-v", "two words"], start.Arguments);
        Assert.Equal("/data", start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void AProgramButtonRunsWithoutASelectionAndUsesItsWorkingFolder()
    {
        var item = ToolbarItem.ForProgram("tool", "--here %s --flag", "/work dir");

        var start = Assert.IsType<ProcessRequest.Argv>(Assert.Single(ToolbarActions.Resolve(item, Context()).Processes));

        Assert.Equal(["--here", "--flag"], start.Arguments);
        Assert.Equal("/work dir", start.WorkingDirectory);
        Assert.Equal(
            ["a.txt"],
            Assert.IsType<ProcessRequest.Argv>(Assert.Single(ToolbarActions.Resolve(ToolbarItem.ForProgram("t"), Context(["a.txt"])).Processes)).Arguments);
    }

    [Fact]
    public void AUserCommandButtonFollowsTheUserCommandsRules()
    {
        var context = Context(["/data/a.txt", "/data/b.txt"], [new UserCommand("List", "ls -l %s", true)]);

        var action = ToolbarActions.Resolve(ToolbarItem.ForUserCommand("list"), context);

        Assert.Equal(2, action.Processes.Count);
        Assert.Equal(["-l", "/data/a.txt"], ((ProcessRequest.Argv)action.Processes[0]).Arguments);
        Assert.Equal(["-l", "/data/b.txt"], ((ProcessRequest.Argv)action.Processes[1]).Arguments);
        Assert.True(ToolbarActions.Resolve(ToolbarItem.ForUserCommand("gone"), context).IsNothing);
        Assert.True(ToolbarActions.Resolve(ToolbarItem.Separator, context).IsNothing);
        Assert.True(ToolbarActions.Resolve(ToolbarItem.ForProgram(" "), context).IsNothing);
    }

    [Fact]
    public void AtComputerAUserCommandAndAProgramStartInHome()
    {
        var context = new ToolbarContext(["a.txt"], ComputerAddress.Text, [new UserCommand("List", "ls %s", true)]);
        var home = LocalFileSystem.HomeDirectory();

        var userCommand = ToolbarActions.Resolve(ToolbarItem.ForUserCommand("list"), context);
        Assert.Equal(home, ((ProcessRequest.Argv)Assert.Single(userCommand.Processes)).WorkingDirectory);

        var program = ToolbarActions.Resolve(ToolbarItem.ForProgram("tool"), context);
        Assert.Equal(home, ((ProcessRequest.Argv)Assert.Single(program.Processes)).WorkingDirectory);
    }

    [Fact]
    public void MoveAndRemoveKeepTheOrderRules()
    {
        var items = ToolbarDefaults.Items();
        var first = items[0];
        var second = items[1];

        Assert.Equal(0, ToolbarItems.Move(items, 0, -1));
        Assert.Equal(1, ToolbarItems.Move(items, 0, 1));
        Assert.Same(second, items[0]);
        Assert.Same(first, items[1]);
        Assert.Equal(items.Count - 1, ToolbarItems.Move(items, items.Count - 1, 1));
        Assert.Equal(-1, ToolbarItems.Move(items, -1, 1));

        var count = items.Count;
        Assert.Equal(1, ToolbarItems.Remove(items, 1));
        Assert.Equal(count - 1, items.Count);
        var last = items.Count - 1;
        Assert.Equal(last - 1, ToolbarItems.Remove(items, last));
        Assert.Equal(-1, ToolbarItems.Remove(items, 99));
        var one = new List<ToolbarItem> { ToolbarItem.Separator };
        Assert.Equal(-1, ToolbarItems.Remove(one, 0));
        Assert.Empty(one);
    }

    private static ToolbarContext Context(string[]? files = null, UserCommand[]? commands = null) =>
        new(files ?? [], "/data", commands ?? []);
}
